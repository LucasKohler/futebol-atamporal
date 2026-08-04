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
    private readonly AppDbContext _db;
    private readonly IMatchService _matches;

    public RegistrationService(AppDbContext db, IMatchService matches)
    {
        _db = db;
        _matches = matches;
    }

    public async Task<MatchDetailDto> JoinAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        var (_, profile) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        RegistrationPolicy.EnsureCanRegister(match.Status);

        var existing = await _db.MatchRegistrations
            .FirstOrDefaultAsync(r => r.MatchId == matchId && r.PlayerProfileId == profile.Id, ct);

        if (existing is { Status: RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted })
            throw new DomainException("Você já está na lista desta partida.");

        var eligibleMonthly = await IsEligibleMonthlyAsync(match.GroupId, profile.Id, ct);
        var priority = eligibleMonthly ? PriorityClass.Monthly : PriorityClass.Casual;

        var confirmedCount = await _db.MatchRegistrations.CountAsync(
            r => r.MatchId == matchId && r.Status == RegistrationStatus.Confirmed, ct);
        var waitCount = await _db.MatchRegistrations.CountAsync(
            r => r.MatchId == matchId && r.Status == RegistrationStatus.Waitlisted, ct);

        var (status, position) = WaitlistLogic.Place(confirmedCount, match.PlayerLimit, waitCount);

        if (existing is not null)
        {
            existing.Status = status;
            existing.PriorityClass = priority;
            existing.WaitlistPosition = position;
            existing.ConfirmedAt = status == RegistrationStatus.Confirmed ? DateTimeOffset.UtcNow : null;
            existing.WithdrawnAt = null;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _db.MatchRegistrations.Add(new MatchRegistration
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                PlayerProfileId = profile.Id,
                Status = status,
                PriorityClass = priority,
                WaitlistPosition = position,
                ConfirmedAt = status == RegistrationStatus.Confirmed ? DateTimeOffset.UtcNow : null
            });
        }

        await _db.SaveChangesAsync(ct);

        // Corrige corrida: se passou do limite, demove este jogador para a fila
        if (status == RegistrationStatus.Confirmed)
        {
            var confirmedNow = await _db.MatchRegistrations.CountAsync(
                r => r.MatchId == matchId && r.Status == RegistrationStatus.Confirmed, ct);
            if (confirmedNow > match.PlayerLimit)
            {
                var mine = await _db.MatchRegistrations
                    .FirstAsync(r => r.MatchId == matchId && r.PlayerProfileId == profile.Id, ct);
                var waitNow = await _db.MatchRegistrations.CountAsync(
                    r => r.MatchId == matchId && r.Status == RegistrationStatus.Waitlisted, ct);
                mine.Status = RegistrationStatus.Waitlisted;
                mine.WaitlistPosition = waitNow + 1;
                mine.ConfirmedAt = null;
                mine.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
        return (await _matches.GetAsync(userId, matchId, ct))!;
    }

    public async Task<MatchDetailDto> LeaveAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        var (_, profile) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        RegistrationPolicy.EnsureCanWithdraw(match.Status);

        var reg = await _db.MatchRegistrations
            .FirstOrDefaultAsync(r => r.MatchId == matchId && r.PlayerProfileId == profile.Id, ct)
            ?? throw new DomainException("Você não está inscrito nesta partida.");

        if (reg.Status is not (RegistrationStatus.Confirmed or RegistrationStatus.Waitlisted))
            throw new DomainException("Inscrição já encerrada.");

        var wasConfirmed = reg.Status == RegistrationStatus.Confirmed;
        reg.Status = RegistrationStatus.Withdrawn;
        reg.WaitlistPosition = null;
        reg.WithdrawnAt = DateTimeOffset.UtcNow;
        reg.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (wasConfirmed)
            await PromoteNextAsync(match, ct);
        else
            await ReindexWaitlistAsync(matchId, ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await _matches.GetAsync(userId, matchId, ct))!;
    }

    public async Task<MatchDetailDto> AdminRemoveAsync(Guid userId, Guid matchId, Guid playerProfileId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);

        var reg = await _db.MatchRegistrations
            .FirstOrDefaultAsync(r => r.MatchId == matchId && r.PlayerProfileId == playerProfileId, ct)
            ?? throw new DomainException("Inscrição não encontrada.");

        var wasConfirmed = reg.Status == RegistrationStatus.Confirmed;
        reg.Status = RegistrationStatus.Removed;
        reg.WaitlistPosition = null;
        reg.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (wasConfirmed)
            await PromoteNextAsync(match, ct);
        else
            await ReindexWaitlistAsync(matchId, ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await _matches.GetAsync(userId, matchId, ct))!;
    }

    /// <summary>Promove o próximo da fila: mensalistas antes de avulsos, depois posição.</summary>
    private async Task PromoteNextAsync(Match match, CancellationToken ct)
    {
        var confirmed = await _db.MatchRegistrations.CountAsync(
            r => r.MatchId == match.Id && r.Status == RegistrationStatus.Confirmed, ct);
        if (confirmed >= match.PlayerLimit)
            return;

        var next = await _db.MatchRegistrations
            .Where(r => r.MatchId == match.Id && r.Status == RegistrationStatus.Waitlisted)
            .OrderByDescending(r => r.PriorityClass) // Monthly = 1 before Casual = 0
            .ThenBy(r => r.WaitlistPosition)
            .ThenBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (next is null) return;

        next.Status = RegistrationStatus.Confirmed;
        next.WaitlistPosition = null;
        next.ConfirmedAt = DateTimeOffset.UtcNow;
        next.UpdatedAt = DateTimeOffset.UtcNow;
        await ReindexWaitlistAsync(match.Id, ct);
    }

    private async Task ReindexWaitlistAsync(Guid matchId, CancellationToken ct)
    {
        var list = await _db.MatchRegistrations
            .Where(r => r.MatchId == matchId && r.Status == RegistrationStatus.Waitlisted)
            .OrderByDescending(r => r.PriorityClass)
            .ThenBy(r => r.WaitlistPosition)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);
        for (var i = 0; i < list.Count; i++)
        {
            list[i].WaitlistPosition = i + 1;
            list[i].UpdatedAt = DateTimeOffset.UtcNow;
        }
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
        return payment.Status is PaymentStatus.Paid or PaymentStatus.Exempt or PaymentStatus.Pending;
        // Pending still eligible; only Overdue/Cancelled lose priority
    }
}
