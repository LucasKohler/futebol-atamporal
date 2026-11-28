using System.Globalization;
using System.Text.Json;
using FutebolAtemporal.Application.Matches;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class RegistrationService : IRegistrationService
{
    private const int MaxAttempts = 5;

    private readonly AppDbContext _db;
    private readonly IMatchService _matches;

    private const string AuditType = "MatchWaitlist";
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock;

    public RegistrationService(AppDbContext db, IMatchService matches, TimeProvider clock)
    {
        _db = db;
        _matches = matches;
        _clock = clock;
    }

    public Task<MatchDetailDto> JoinAsync(Guid userId, Guid matchId, JoinMatchRequest? request, CancellationToken ct = default)
        => MutateListAsync(userId, matchId, async (match, regs) =>
        {
            var (member, profile) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
            if (IsActive(regs.FirstOrDefault(r => r.PlayerProfileId == profile.Id)))
                return false;

            RegistrationPolicy.EnsureCanRegister(match.Status);
            var now = _clock.GetUtcNow();
            var monthly = await RegistrationEligibility.IsMonthlyAsync(_db, match.GroupId, profile.Id, now, ct);
            var opensAt = RegistrationWindowPolicy.OpensAt(match, monthly);
            if (opensAt is { } opening && now < opening)
            {
                if (member.Role != GroupRole.Admin)
                    throw new RegistrationWindowClosedException(match.MonthlyRegistrationOpensAt <= now, opening);

                var reason = ValidateReason(request?.Reason);
                var before = Snapshot(match, regs);
                var earlyEntry = Enroll(match, regs, profile.Id, monthly, now);
                WaitlistLogic.SetPosition(regs, earlyEntry, null, now);
                Audit(userId, match, regs, "AdminEarlySelfJoin", reason, before, now,
                    new { playerProfileId = profile.Id, earlyEntry.WaitlistPosition });
                return true;
            }

            var entry = Enroll(match, regs, profile.Id, monthly, now);
            if (NormalPlacement(match, regs) == RegistrationStatus.Confirmed)
                WaitlistLogic.Confirm(entry, now);
            else
                WaitlistLogic.SetPosition(regs, entry, null, now);
            return true;
        }, ct);

    public Task<MatchDetailDto> UpdateWindowsAsync(
        Guid userId, Guid matchId, UpdateRegistrationWindowsRequest request, CancellationToken ct)
        => AdminChangeAsync(userId, matchId, request.ExpectedVersion, request.Reason, "MatchRegistrationWindowsUpdated",
            (match, _, now) =>
            {
                var monthly = ParseOpening(request.MonthlyOpensAt);
                var casual = ParseOpening(request.CasualOpensAt);
                RegistrationWindowPolicy.Validate(monthly, casual, match.StartsAt);
                match.MonthlyRegistrationOpensAt = monthly;
                match.CasualRegistrationOpensAt = casual;
                match.UpdatedAt = now;
                return Task.FromResult<object>(new { });
            }, ct);

    public Task<MatchDetailDto> MoveAsync(Guid userId, Guid matchId, MoveWaitlistRequest request, CancellationToken ct)
        => AdminChangeAsync(userId, matchId, request.ExpectedVersion, request.Reason, "WaitlistReordered",
            (_, regs, now) =>
            {
                var registration = RequireStatus(regs, request.PlayerProfileId, RegistrationStatus.Waitlisted);
                var fromPosition = registration.WaitlistPosition;
                var queueBefore = WaitlistLogic.QueueOrder(regs).Select(r => r.PlayerProfileId).ToArray();
                WaitlistLogic.SetPosition(regs, registration, request.TargetPosition, now);
                return Task.FromResult<object>(new
                {
                    request.PlayerProfileId, FromPosition = fromPosition, ToPosition = request.TargetPosition,
                    QueueBefore = queueBefore,
                    QueueAfter = WaitlistLogic.QueueOrder(regs).Select(r => r.PlayerProfileId).ToArray()
                });
            }, ct);

    public Task<MatchDetailDto> AdminEnrollAsync(Guid userId, Guid matchId, AdminEnrollRequest request, CancellationToken ct)
        => AdminChangeAsync(userId, matchId, request.ExpectedVersion, request.Reason, "AdminEnrolledOutsideWindow",
            async (match, regs, now) =>
            {
                if (request.Placement is not ("Auto" or "Waitlist" or "Confirmed"))
                    throw new DomainException("Escolha uma forma de inscrição válida.");

                var target = await (
                    from p in _db.PlayerProfiles.AsNoTracking()
                    join m in _db.GroupMemberships.AsNoTracking() on p.UserId equals m.UserId
                    where p.Id == request.PlayerProfileId && m.GroupId == match.GroupId && m.Status == MembershipStatus.Active
                    select p
                ).SingleOrDefaultAsync(ct) ?? throw new DomainException("Escolha um membro ativo deste grupo.");

                if (target.UserId == userId)
                    throw new DomainException("Use a ação de inscrição própria para entrar na lista.");
                if (IsActive(regs.FirstOrDefault(r => r.PlayerProfileId == target.Id)))
                    throw new DomainException("Este jogador já está inscrito.");

                var monthly = await RegistrationEligibility.IsMonthlyAsync(_db, match.GroupId, target.Id, now, ct);
                var entry = Enroll(match, regs, target.Id, monthly, now);
                if (request.Placement == "Confirmed" ||
                    request.Placement == "Auto" && NormalPlacement(match, regs) == RegistrationStatus.Confirmed)
                    Promote(match, regs, entry, request.DemotePlayerProfileId, null, now);
                else
                    WaitlistLogic.SetPosition(regs, entry, request.TargetWaitlistPosition, now);

                return new { request.PlayerProfileId, request.Placement, request.DemotePlayerProfileId };
            }, ct);

    public Task<MatchDetailDto> AdjustStatusAsync(
        Guid userId, Guid matchId, Guid playerProfileId, AdjustRegistrationStatusRequest request, CancellationToken ct)
        => AdminChangeAsync(userId, matchId, request.ExpectedVersion, request.Reason, "RegistrationStatusAdjusted",
            (match, regs, now) =>
            {
                switch (request.TargetStatus)
                {
                    case "Confirmed":
                        Promote(match, regs, RequireStatus(regs, playerProfileId, RegistrationStatus.Waitlisted),
                            request.DemotePlayerProfileId, request.DemoteToPosition, now);
                        break;
                    case "Waitlisted":
                        var registration = RequireStatus(regs, playerProfileId, RegistrationStatus.Confirmed);
                        WaitlistLogic.SetPosition(regs, registration, request.TargetWaitlistPosition, now);
                        if (request.PromoteNext != false && WaitlistLogic.QueueOrder(regs).FirstOrDefault(r => r != registration) is { } next)
                        {
                            WaitlistLogic.Confirm(next, now);
                            WaitlistLogic.Renumber(WaitlistLogic.QueueOrder(regs), now);
                        }
                        break;
                    default:
                        throw new DomainException("Escolha Confirmed ou Waitlisted para a inscrição.");
                }
                return Task.FromResult<object>(new
                {
                    PlayerProfileId = playerProfileId, request.TargetStatus, request.DemotePlayerProfileId
                });
            }, ct);

    public async Task<IReadOnlyList<WaitlistAuditDto>> GetAuditAsync(
        Guid userId, Guid matchId, int limit, int offset, CancellationToken ct)
    {
        var match = await _db.Matches.AsNoTracking().SingleOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new NotFoundException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        if (limit is < 1 or > 100 || offset < 0)
            throw new DomainException("Paginação inválida.");

        // SQLite armazena DateTimeOffset como texto. Estes eventos usam sempre o mesmo offset UTC.
        var entries = await _db.AuditEntries.FromSqlInterpolated($"""
            SELECT "Id", "GroupId", "ActorUserId", "Action", "EntityType", "EntityId", "PayloadJson", "CreatedAt"
            FROM "AuditEntries"
            WHERE "EntityType" = {AuditType} AND "EntityId" = {matchId} AND "GroupId" = {match.GroupId}
            ORDER BY "CreatedAt" DESC, "Id" DESC
            LIMIT {limit} OFFSET {offset}
            """).AsNoTracking().ToListAsync(ct);
        return entries.Select(entry =>
        {
            var payload = JsonSerializer.Deserialize<AuditPayload>(entry.PayloadJson, AuditJson)!;
            return new WaitlistAuditDto(entry.Id, entry.ActorUserId, entry.CreatedAt, entry.Action, payload.Reason,
                payload.Version, payload.Before, payload.After, payload.Details);
        }).ToList();
    }

    private Task<MatchDetailDto> AdminChangeAsync(
        Guid userId, Guid matchId, int? expectedVersion, string? reason, string action,
        Func<Match, List<MatchRegistration>, DateTimeOffset, Task<object>> change, CancellationToken ct)
        => MutateListAsync(userId, matchId, async (match, regs) =>
        {
            await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
            if (expectedVersion is null or < 0)
                throw new DomainException("Informe a versão da lista.");
            if (match.Version != expectedVersion)
                throw VersionConflict();

            RegistrationPolicy.EnsureCanWithdraw(match.Status);
            var note = ValidateReason(reason);
            var now = _clock.GetUtcNow();
            var before = Snapshot(match, regs);
            var details = await change(match, regs, now);
            Audit(userId, match, regs, action, note, before, now, details);
            return true;
        }, ct, retry: false);

    private static ConflictException VersionConflict()
        => new("A lista mudou. Atualize e confirme a ação novamente.", "waitlist_version_conflict");

    private static DateTimeOffset? ParseOpening(string? value)
    {
        if (value is null) return null;

        var withOffset = value.EndsWith('Z') ? value[..^1] + "+00:00" : value;
        if (!DateTimeOffset.TryParseExact(withOffset, ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mmzzz"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var opening))
            throw new DomainException("Informe o horário em ISO 8601 com fuso explícito.", "invalid_registration_windows");
        return opening.ToUniversalTime();
    }

    private static string ValidateReason(string? reason)
    {
        var text = reason?.Trim();
        if (text is null || text.Length is < 5 or > 300)
            throw new DomainException("Informe um motivo de 5 a 300 caracteres.", "waitlist_reason_required");
        return text;
    }

    private static int ConfirmedCount(IEnumerable<MatchRegistration> regs)
        => regs.Count(r => r.Status == RegistrationStatus.Confirmed);

    private static RegistrationStatus NormalPlacement(Match match, List<MatchRegistration> regs)
        => WaitlistLogic.Place(ConfirmedCount(regs), match.PlayerLimit, regs.Count(r => r.Status == RegistrationStatus.Waitlisted)).Status;

    private static bool IsActive(MatchRegistration? registration)
        => registration?.Status is RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted;

    private static MatchRegistration RequireStatus(
        IEnumerable<MatchRegistration> regs, Guid playerProfileId, RegistrationStatus status)
        => regs.FirstOrDefault(r => r.PlayerProfileId == playerProfileId && r.Status == status)
            ?? throw new DomainException("A inscrição selecionada não está no estado necessário para esta ação.");

    private static void Promote(
        Match match, List<MatchRegistration> regs, MatchRegistration registration,
        Guid? demotePlayerProfileId, int? demoteToPosition, DateTimeOffset now)
    {
        var full = ConfirmedCount(regs) >= match.PlayerLimit;
        if (full && demotePlayerProfileId is null)
            throw new DomainException("A lista está cheia. Escolha um confirmado para trocar de posição.", "waitlist_swap_required");
        if (!full && demotePlayerProfileId is not null)
            throw new DomainException("A lista tem vaga. Atualize a seleção antes de confirmar a promoção.");

        var replaced = demotePlayerProfileId is { } demote
            ? RequireStatus(regs, demote, RegistrationStatus.Confirmed) : null;
        WaitlistLogic.Confirm(registration, now);
        if (replaced is not null)
            WaitlistLogic.SetPosition(regs, replaced, demoteToPosition, now);
        else
            WaitlistLogic.Renumber(WaitlistLogic.QueueOrder(regs), now);
    }

    private MatchRegistration Enroll(
        Match match, List<MatchRegistration> regs, Guid playerProfileId, bool monthly, DateTimeOffset now)
    {
        var entry = regs.FirstOrDefault(r => r.PlayerProfileId == playerProfileId);
        if (entry is null)
        {
            entry = new MatchRegistration { Id = Guid.NewGuid(), MatchId = match.Id, PlayerProfileId = playerProfileId, CreatedAt = now };
            _db.MatchRegistrations.Add(entry);
            regs.Add(entry);
        }
        // A posição é atribuída explicitamente por quem chama, inclusive na reentrada.
        entry.Status = RegistrationStatus.Withdrawn;
        entry.PriorityClass = monthly ? PriorityClass.Monthly : PriorityClass.Casual;
        entry.WaitlistPosition = null;
        entry.WithdrawnAt = null;
        entry.UpdatedAt = now;
        return entry;
    }

    private static JsonElement Snapshot(Match match, List<MatchRegistration> regs) => JsonSerializer.SerializeToElement(new
    {
        match.MonthlyRegistrationOpensAt,
        match.CasualRegistrationOpensAt,
        Confirmed = regs.Where(r => r.Status == RegistrationStatus.Confirmed).Select(r => r.PlayerProfileId).Order().ToArray(),
        Waitlist = WaitlistLogic.QueueOrder(regs).Select(r => new { r.PlayerProfileId, r.WaitlistPosition }).ToArray()
    }, AuditJson);

    private void Audit(Guid userId, Match match, List<MatchRegistration> regs, string action, string reason,
        JsonElement before, DateTimeOffset now, object details)
        => _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(), GroupId = match.GroupId, ActorUserId = userId, Action = action,
            EntityType = AuditType, EntityId = match.Id, CreatedAt = now,
            PayloadJson = JsonSerializer.Serialize(new AuditPayload(1, reason, match.Version + 1, before,
                Snapshot(match, regs), JsonSerializer.SerializeToElement(details, AuditJson)), AuditJson)
        });

    private sealed record AuditPayload(
        int SchemaVersion, string Reason, int Version, JsonElement Before, JsonElement After, JsonElement Details);

    public Task<MatchDetailDto> LeaveAsync(Guid userId, Guid matchId, CancellationToken ct = default)
        => MutateListAsync(userId, matchId, async (match, regs) =>
        {
            var (_, profile) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
            RegistrationPolicy.EnsureCanWithdraw(match.Status);

            var reg = regs.FirstOrDefault(r => r.PlayerProfileId == profile.Id)
                ?? throw new DomainException("Você não está inscrito nesta partida.");
            if (reg.Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted))
                throw new DomainException("Inscrição já encerrada.");

            var now = _clock.GetUtcNow();
            var releasedSpot = reg.Status == RegistrationStatus.Confirmed;
            reg.Status = RegistrationStatus.Withdrawn;
            reg.WaitlistPosition = null;
            reg.WithdrawnAt = now;
            reg.UpdatedAt = now;
            if (releasedSpot)
                WaitlistLogic.Rebalance(regs, match.PlayerLimit, now);
            else
                WaitlistLogic.Renumber(WaitlistLogic.QueueOrder(regs), now);
            return true;
        }, ct);

    public Task<MatchDetailDto> AdminRemoveAsync(Guid userId, Guid matchId, Guid playerProfileId, CancellationToken ct = default)
        => MutateListAsync(userId, matchId, async (match, regs) =>
        {
            await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
            RegistrationPolicy.EnsureCanWithdraw(match.Status);

            var reg = regs.FirstOrDefault(r => r.PlayerProfileId == playerProfileId)
                ?? throw new DomainException("Inscrição não encontrada.");
            if (reg.Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted))
                throw new DomainException("Inscrição já encerrada.");

            var now = _clock.GetUtcNow();
            var releasedSpot = reg.Status == RegistrationStatus.Confirmed;
            reg.Status = RegistrationStatus.Removed;
            reg.WaitlistPosition = null;
            reg.UpdatedAt = now;
            if (releasedSpot)
                WaitlistLogic.Rebalance(regs, match.PlayerLimit, now);
            else
                WaitlistLogic.Renumber(WaitlistLogic.QueueOrder(regs), now);
            return true;
        }, ct);

    /// <summary>
    /// Executa uma alteração da lista numa transação e incrementa <see cref="Match.Version"/> (token de concorrência).
    /// Duas alterações simultâneas na mesma partida: a segunda falha no SaveChanges, é desfeita e repetida
    /// com os dados novos. Assim a última vaga não é ocupada duas vezes, em SQLite ou PostgreSQL.
    /// </summary>
    private async Task<MatchDetailDto> MutateListAsync(
        Guid userId, Guid matchId, Func<Match, List<MatchRegistration>, Task<bool>> mutate, CancellationToken ct, bool retry = true)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
                    ?? throw new NotFoundException("Partida não encontrada.");
                var regs = await _db.MatchRegistrations.Where(r => r.MatchId == matchId).ToListAsync(ct);

                var roster = regs.Where(r => r.Status == RegistrationStatus.Confirmed).Select(r => r.PlayerProfileId).ToHashSet();
                if (await mutate(match, regs))
                {
                    // Lista mudou depois do sorteio: a escalação precisa ser refeita ou revisada (PR-3).
                    var updatedRoster = regs.Where(r => r.Status == RegistrationStatus.Confirmed).Select(r => r.PlayerProfileId);
                    if (!roster.SetEquals(updatedRoster))
                        await DrawRosterGuard.InvalidateIfRosterChangedAsync(_db, match, updatedRoster, ct);
                    match.Version++;
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
                break;
            }
            catch (DbUpdateConcurrencyException) when (!retry || attempt >= MaxAttempts)
            {
                throw VersionConflict();
            }
            catch (DbUpdateException) when (retry && attempt < MaxAttempts)
            {
                // Conflito de versão ou inscrição duplicada (índice único): recomeça com estado atualizado.
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
            }
        }

        _db.ChangeTracker.Clear();
        return (await _matches.GetAsync(userId, matchId, ct))!;
    }

}
