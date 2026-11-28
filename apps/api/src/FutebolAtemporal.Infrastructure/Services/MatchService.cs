using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Application.Matches;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class MatchService : IMatchService
{
    private readonly AppDbContext _db;
    private readonly INotificationTriggerService? _notifications;

    public MatchService(AppDbContext db, INotificationTriggerService? notifications = null)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<MatchDto> CreateAsync(Guid userId, CreateMatchRequest request, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, request.GroupId, ct, adminOnly: true);
        if (request.PlayerLimit < 2)
            throw new DomainException("Limite de jogadores deve ser pelo menos 2.");

        var match = new Match
        {
            Id = Guid.NewGuid(),
            GroupId = request.GroupId,
            Title = request.Title,
            StartsAt = request.StartsAt,
            Location = request.Location.Trim(),
            PlayerLimit = request.PlayerLimit,
            Status = request.OpenRegistrations ? MatchStatus.RegistrationOpen : MatchStatus.Draft
        };
        _db.Matches.Add(match);
        if (request.OpenRegistrations && _notifications is not null)
            await _notifications.EnqueueMatchOpenAsync(match.GroupId, match.Id, userId, ct);
        await _db.SaveChangesAsync(ct);
        if (request.OpenRegistrations) _notifications?.SignalDispatcher();
        return ToDto(match, 0, 0);
    }

    public async Task<MatchDto> UpdateAsync(Guid userId, Guid matchId, UpdateMatchRequest request, CancellationToken ct = default)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        if (match.Status is MatchStatus.Completed or MatchStatus.Cancelled)
            throw new DomainException("Partida encerrada não pode ser editada.");

        if (request.PlayerLimit < 2)
            throw new DomainException("Limite de jogadores deve ser pelo menos 2.");
        // Elenco congelado depois do início: título, local e observações podem mudar; o limite não (promoveria da fila).
        if (RegistrationPolicy.IsLineupFrozen(match.Status) && request.PlayerLimit != match.PlayerLimit)
            throw new DomainException("A partida já começou: o limite de jogadores não pode mais mudar, para preservar o elenco.");

        var regs = await _db.MatchRegistrations.Where(r => r.MatchId == matchId).ToListAsync(ct);
        var confirmed = regs.Count(r => r.Status == RegistrationStatus.Confirmed);
        if (request.PlayerLimit < confirmed)
            throw new DomainException($"Há {confirmed} confirmados. Remova inscrições antes de reduzir o limite para {request.PlayerLimit}.");

        match.Title = request.Title;
        match.StartsAt = request.StartsAt;
        match.Location = request.Location.Trim();
        match.PlayerLimit = request.PlayerLimit;
        match.Notes = request.Notes;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        // Limite maior promove a fila na ordem publicada.
        var promoted = WaitlistLogic.Rebalance(regs, match.PlayerLimit, match.UpdatedAt.Value);
        // Promoção por aumento de limite depois do sorteio invalida a escalação confirmada.
        await DrawRosterGuard.InvalidateIfRosterChangedAsync(
            _db, match, regs.Where(r => r.Status == RegistrationStatus.Confirmed).Select(r => r.PlayerProfileId), ct);
        match.Version++;
        if (promoted.Count > 0 && _notifications is not null)
            await _notifications.EnqueueWaitlistPromotedAsync(match.GroupId, match.Id, promoted.Select(p => (p.Id, p.PlayerProfileId, p.ConfirmedAt ?? match.UpdatedAt!.Value)).ToList(), ct);
        await _db.SaveChangesAsync(ct);
        if (promoted.Count > 0) _notifications?.SignalDispatcher();
        return await ToDtoAsync(match, ct);
    }

    public async Task<MatchDto> TransitionAsync(Guid userId, Guid matchId, MatchStatus target, CancellationToken ct = default)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        MatchTransitions.EnsureTransition(match.Status, target);
        match.Status = target;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++;
        if (target == MatchStatus.RegistrationOpen && _notifications is not null)
            await _notifications.EnqueueMatchOpenAsync(match.GroupId, match.Id, userId, ct);
        await _db.SaveChangesAsync(ct);
        if (target == MatchStatus.RegistrationOpen) _notifications?.SignalDispatcher();
        return await ToDtoAsync(match, ct);
    }

    public async Task<MatchDetailDto?> GetAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        var match = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return null;
        await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        return await BuildDetailAsync(userId, match, ct);
    }

    public async Task<MatchDetailDto?> GetUpcomingAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);
        var now = DateTimeOffset.UtcNow;
        var candidates = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status != MatchStatus.Cancelled && m.Status != MatchStatus.Completed)
            .ToListAsync(ct);
        var match = MatchSelection.Upcoming(candidates, now);

        if (match is null) return null;
        return await BuildDetailAsync(userId, match, ct);
    }

    public async Task<IReadOnlyList<MatchDto>> ListAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);
        var matches = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .ToListAsync(ct);
        matches = matches.OrderByDescending(m => m.StartsAt).Take(50).ToList();

        var result = new List<MatchDto>();
        foreach (var m in matches)
            result.Add(await ToDtoAsync(m, ct));
        return result;
    }

    private async Task<MatchDetailDto> BuildDetailAsync(Guid userId, Match match, CancellationToken ct)
    {
        var regs = await _db.MatchRegistrations.AsNoTracking()
            .Where(r => r.MatchId == match.Id &&
                        (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Waitlisted))
            .ToListAsync(ct);

        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, regs.Select(r => r.PlayerProfileId), ct);
        // Notas são dado administrativo: jogador comum recebe a lista sem elas.
        var isAdmin = await _db.GroupMemberships.AsNoTracking().AnyAsync(
            m => m.UserId == userId && m.GroupId == match.GroupId
                 && m.Status == MembershipStatus.Active && m.Role == GroupRole.Admin, ct);
        var profileIds = regs.Select(r => r.PlayerProfileId).ToList();
        var skills = isAdmin
            ? await _db.PlayerProfiles.AsNoTracking()
                .Where(p => profileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (decimal?)p.SkillRating, ct)
            : new Dictionary<Guid, decimal?>();

        RegistrationPlayerDto Map(MatchRegistration r) => new(
            r.Id, r.PlayerProfileId, names.GetValueOrDefault(r.PlayerProfileId, "Jogador"),
            r.Status.ToString(), r.PriorityClass.ToString(), r.WaitlistPosition,
            skills.GetValueOrDefault(r.PlayerProfileId));

        // Ordem persistida: confirmados por momento da confirmação; fila pela posição publicada.
        var confirmed = regs.Where(r => r.Status == RegistrationStatus.Confirmed)
            .OrderBy(r => r.ConfirmedAt?.UtcTicks ?? 0).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(Map).ToList();
        var waitlist = WaitlistLogic.QueueOrder(regs).Select(Map).ToList();

        var profile = await _db.PlayerProfiles.AsNoTracking().FirstAsync(p => p.UserId == userId, ct);
        var mine = regs.FirstOrDefault(r => r.PlayerProfileId == profile.Id);

        return new MatchDetailDto(
            ToDto(match, confirmed.Count, waitlist.Count),
            mine?.Status.ToString(),
            mine?.WaitlistPosition,
            confirmed,
            waitlist);
    }

    private async Task<MatchDto> ToDtoAsync(Match match, CancellationToken ct)
    {
        var confirmed = await _db.MatchRegistrations.CountAsync(r => r.MatchId == match.Id && r.Status == RegistrationStatus.Confirmed, ct);
        var wait = await _db.MatchRegistrations.CountAsync(r => r.MatchId == match.Id && r.Status == RegistrationStatus.Waitlisted, ct);
        return ToDto(match, confirmed, wait);
    }

    private static MatchDto ToDto(Match match, int confirmed, int wait) => new(
        match.Id, match.GroupId, match.Title, match.StartsAt, match.Location, match.PlayerLimit,
        match.Status.ToString(), match.Notes, confirmed, wait, Math.Max(0, match.PlayerLimit - confirmed));
}
