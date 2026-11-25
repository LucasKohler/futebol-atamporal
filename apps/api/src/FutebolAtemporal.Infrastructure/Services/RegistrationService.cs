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

    public RegistrationService(AppDbContext db, IMatchService matches)
    {
        _db = db;
        _matches = matches;
    }

    public Task<MatchDetailDto> JoinAsync(Guid userId, Guid matchId, CancellationToken ct = default)
        => MutateListAsync(userId, matchId, async (match, regs) =>
        {
            var (_, profile) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
            RegistrationPolicy.EnsureCanRegister(match.Status);

            var existing = regs.FirstOrDefault(r => r.PlayerProfileId == profile.Id);
            // Repetição do mesmo pedido (retry, duplo toque) devolve o estado atual sem duplicar.
            if (existing is { Status: RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted })
                return false;

            var now = DateTimeOffset.UtcNow;
            var priority = await IsEligibleMonthlyAsync(match.GroupId, profile.Id, ct) ? PriorityClass.Monthly : PriorityClass.Casual;
            var confirmed = regs.Count(r => r.Status == RegistrationStatus.Confirmed);
            var waiting = regs.Count(r => r.Status == RegistrationStatus.Waitlisted);
            var (status, position) = WaitlistLogic.Place(confirmed, match.PlayerLimit, waiting);

            if (existing is null)
            {
                existing = new MatchRegistration
                {
                    Id = Guid.NewGuid(),
                    MatchId = match.Id,
                    PlayerProfileId = profile.Id,
                    CreatedAt = now
                };
                _db.MatchRegistrations.Add(existing);
                regs.Add(existing);
            }

            existing.Status = status;
            existing.PriorityClass = priority;
            existing.WaitlistPosition = position;
            existing.ConfirmedAt = status == RegistrationStatus.Confirmed ? now : null;
            existing.WithdrawnAt = null;
            existing.UpdatedAt = now;

            WaitlistLogic.Rebalance(regs, match.PlayerLimit, now);
            return true;
        }, ct);

    public Task<MatchDetailDto> LeaveAsync(Guid userId, Guid matchId, CancellationToken ct = default)
        => MutateListAsync(userId, matchId, async (match, regs) =>
        {
            var (_, profile) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
            RegistrationPolicy.EnsureCanWithdraw(match.Status);

            var reg = regs.FirstOrDefault(r => r.PlayerProfileId == profile.Id)
                ?? throw new DomainException("Você não está inscrito nesta partida.");
            if (reg.Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted))
                throw new DomainException("Inscrição já encerrada.");

            var now = DateTimeOffset.UtcNow;
            reg.Status = RegistrationStatus.Withdrawn;
            reg.WaitlistPosition = null;
            reg.WithdrawnAt = now;
            reg.UpdatedAt = now;
            WaitlistLogic.Rebalance(regs, match.PlayerLimit, now);
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

            var now = DateTimeOffset.UtcNow;
            reg.Status = RegistrationStatus.Removed;
            reg.WaitlistPosition = null;
            reg.UpdatedAt = now;
            WaitlistLogic.Rebalance(regs, match.PlayerLimit, now);
            return true;
        }, ct);

    /// <summary>
    /// Executa uma alteração da lista numa transação e incrementa <see cref="Match.Version"/> (token de concorrência).
    /// Duas alterações simultâneas na mesma partida: a segunda falha no SaveChanges, é desfeita e repetida
    /// com os dados novos. Assim a última vaga não é ocupada duas vezes, em SQLite ou PostgreSQL.
    /// </summary>
    private async Task<MatchDetailDto> MutateListAsync(
        Guid userId, Guid matchId, Func<Match, List<MatchRegistration>, Task<bool>> mutate, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
                    ?? throw new DomainException("Partida não encontrada.");
                var regs = await _db.MatchRegistrations.Where(r => r.MatchId == matchId).ToListAsync(ct);

                if (await mutate(match, regs))
                {
                    // Lista mudou depois do sorteio: a escalação precisa ser refeita ou revisada (PR-3).
                    await DrawRosterGuard.InvalidateIfRosterChangedAsync(
                        _db, match, regs.Where(r => r.Status == RegistrationStatus.Confirmed).Select(r => r.PlayerProfileId), ct);
                    match.Version++;
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
                break;
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Conflito de versão ou inscrição duplicada (índice único): recomeça com estado atualizado.
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
            }
        }

        _db.ChangeTracker.Clear();
        return (await _matches.GetAsync(userId, matchId, ct))!;
    }

    private async Task<bool> IsEligibleMonthlyAsync(Guid groupId, Guid playerProfileId, CancellationToken ct)
    {
        var membership = await _db.MonthlyMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.PlayerProfileId == playerProfileId
                                      && m.Status == MonthlyMembershipStatus.Active, ct);
        if (membership is null) return false;

        var now = DateTimeOffset.UtcNow;
        var period = await _db.MembershipBillingPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.GroupId == groupId && p.Year == now.Year && p.Month == now.Month, ct);
        if (period is null) return true;

        var payment = await _db.MembershipPayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MonthlyMembershipId == membership.Id && p.BillingPeriodId == period.Id, ct);
        if (payment is null) return true;
        // Pending segue elegível; só Overdue/Cancelled perdem prioridade.
        return payment.Status is PaymentStatus.Paid or PaymentStatus.Exempt or PaymentStatus.Pending;
    }
}
