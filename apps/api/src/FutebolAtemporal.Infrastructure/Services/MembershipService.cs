using FutebolAtemporal.Application.Payments;
using FutebolAtemporal.Application.Memberships;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class MembershipService : IMembershipService
{
    private readonly AppDbContext _db;

    public MembershipService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MonthlyMemberDto>> ListMembersAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        var members = await _db.MonthlyMemberships.AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, members.Select(m => m.PlayerProfileId), ct);
        return members.Select(m => new MonthlyMemberDto(
            m.Id, m.PlayerProfileId, names.GetValueOrDefault(m.PlayerProfileId, "Jogador"),
            m.Status.ToString(), m.StartedAt)).ToList();
    }

    public async Task<MonthlyMemberDto> AddMemberAsync(Guid userId, Guid groupId, Guid playerProfileId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        _ = await _db.PlayerProfiles.FirstOrDefaultAsync(p => p.Id == playerProfileId, ct)
            ?? throw new DomainException("Jogador não encontrado.");

        var existing = await _db.MonthlyMemberships
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.PlayerProfileId == playerProfileId, ct);
        if (existing is not null)
        {
            existing.Status = MonthlyMembershipStatus.Active;
            existing.EndedAt = null;
            existing.StartedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            existing = new MonthlyMembership
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                PlayerProfileId = playerProfileId,
                Status = MonthlyMembershipStatus.Active
            };
            _db.MonthlyMemberships.Add(existing);
        }

        await EnsureOpenPeriodChargeAsync(existing, userId, ct);
        await _db.SaveChangesAsync(ct);
        var name = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [playerProfileId], ct))[playerProfileId];
        return new MonthlyMemberDto(existing.Id, playerProfileId, name, existing.Status.ToString(), existing.StartedAt);
    }

    public async Task<MonthlyMemberDto> SetStatusAsync(Guid userId, Guid groupId, Guid membershipId, MonthlyMembershipStatus status, CancellationToken ct = default)
    {
        var membership = await RequireMembershipInGroupAsync(userId, groupId, membershipId, ct);
        if (!Enum.IsDefined(status))
            throw new DomainException("Situação do mensalista inválida. Use Active, Inactive ou Suspended.");
        membership.Status = status;
        membership.EndedAt = status == MonthlyMembershipStatus.Active ? null : DateTimeOffset.UtcNow;
        if (status == MonthlyMembershipStatus.Active)
            await EnsureOpenPeriodChargeAsync(membership, userId, ct);
        await _db.SaveChangesAsync(ct);
        var name = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [membership.PlayerProfileId], ct))[membership.PlayerProfileId];
        return new MonthlyMemberDto(membership.Id, membership.PlayerProfileId, name, membership.Status.ToString(), membership.StartedAt);
    }

    public async Task<BillingPeriodDto> EnsurePeriodAsync(Guid userId, Guid groupId, int year, int month, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        if (month is < 1 or > 12) throw new DomainException("Mês inválido.");

        var period = await _db.MembershipBillingPeriods
            .FirstOrDefaultAsync(p => p.GroupId == groupId && p.Year == year && p.Month == month, ct);
        if (period is null)
        {
            period = new MembershipBillingPeriod
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                Year = year,
                Month = month,
                Label = new DateTime(year, month, 1).ToString("MMMM yyyy", new CultureInfo("pt-BR"))
            };
            _db.MembershipBillingPeriods.Add(period);

            var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
            var fee = GroupSettings.Parse(group.SettingsJson).DefaultMonthlyFee;
            var actives = await _db.MonthlyMemberships
                .Where(m => m.GroupId == groupId && m.Status == MonthlyMembershipStatus.Active)
                .ToListAsync(ct);
            foreach (var m in actives)
            {
                _db.MembershipPayments.Add(new MembershipPayment
                {
                    Id = Guid.NewGuid(),
                    MonthlyMembershipId = m.Id,
                    BillingPeriodId = period.Id,
                    Amount = fee,
                    Status = PaymentStatus.Pending,
                    RecordedByUserId = userId
                });
            }
            await _db.SaveChangesAsync(ct);
        }

        return new BillingPeriodDto(period.Id, period.Year, period.Month, period.Label);
    }

    public async Task<IReadOnlyList<PaymentDto>> ListPaymentsAsync(Guid userId, Guid groupId, int? year, int? month, PaymentStatus? status, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        var q = from pay in _db.MembershipPayments.AsNoTracking()
                join mem in _db.MonthlyMemberships.AsNoTracking() on pay.MonthlyMembershipId equals mem.Id
                join per in _db.MembershipBillingPeriods.AsNoTracking() on pay.BillingPeriodId equals per.Id
                where mem.GroupId == groupId
                select new { pay, mem, per };

        if (year.HasValue) q = q.Where(x => x.per.Year == year);
        if (month.HasValue) q = q.Where(x => x.per.Month == month);
        if (status.HasValue) q = q.Where(x => x.pay.Status == status);

        var rows = await q.OrderBy(x => x.per.Year).ThenBy(x => x.per.Month).ThenBy(x => x.pay.Id).ToListAsync(ct);
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, rows.Select(r => r.mem.PlayerProfileId), ct);
        var paymentIds = rows.Select(r => r.pay.Id).ToList();
        var latestPixByPayment = paymentIds.Count == 0
            ? new Dictionary<Guid, PixChargeSummaryDto>()
            : (await _db.PixCharges.AsNoTracking()
                .Where(c => c.MembershipPaymentId != null && paymentIds.Contains(c.MembershipPaymentId.Value))
                .ToListAsync(ct)).OrderByDescending(c => c.CreatedAt)
                .GroupBy(c => c.MembershipPaymentId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var c = g.First();
                        var effStatus = c.Status == PixChargeStatus.Pending && c.ExpiresAt <= DateTimeOffset.UtcNow
                            ? PixChargeStatus.Expired
                            : c.Status;
                        return new PixChargeSummaryDto(c.Id, c.MpPaymentId, effStatus.ToString(), c.Amount, c.ExpiresAt, c.CreatedAt, c.ApprovedAt, c.IsSimulated);
                    });

        return rows.Select(r => new PaymentDto(
            r.pay.Id, r.mem.Id, names.GetValueOrDefault(r.mem.PlayerProfileId, "Jogador"),
            r.per.Id, r.per.Label, r.pay.Amount, r.pay.Status.ToString(), r.pay.PaidAt, r.pay.Notes,
            r.mem.PlayerProfileId, r.pay.PaymentSource.ToString(),
            latestPixByPayment.GetValueOrDefault(r.pay.Id))).ToList();
    }

    public async Task<PaymentDto> UpsertPaymentAsync(Guid userId, Guid groupId, Guid membershipId, Guid periodId, UpsertPaymentRequest request, CancellationToken ct = default)
    {
        var membership = await RequireMembershipInGroupAsync(userId, groupId, membershipId, ct);
        var period = await _db.MembershipBillingPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.GroupId == membership.GroupId, ct)
            ?? throw new DomainException("Competência não encontrada.");
        if (!Enum.IsDefined(request.Status))
            throw new DomainException("Situação do pagamento inválida. Use Pending, Paid, Overdue, Exempt ou Cancelled.");
        if (request.Amount < 0)
            throw new DomainException("O valor da mensalidade não pode ser negativo.");

        var pay = await _db.MembershipPayments
            .FirstOrDefaultAsync(p => p.MonthlyMembershipId == membershipId && p.BillingPeriodId == periodId, ct);
        if (pay is not null && pay.Status == PaymentStatus.Paid && pay.PaymentSource == PaymentSource.Pix)
            throw new ConflictException("Esta mensalidade já foi quitada via Pix e não pode ser alterada manualmente.");

        if (pay is null)
        {
            pay = new MembershipPayment
            {
                Id = Guid.NewGuid(),
                MonthlyMembershipId = membershipId,
                BillingPeriodId = periodId,
                RecordedByUserId = userId
            };
            _db.MembershipPayments.Add(pay);
        }

        pay.Amount = request.Amount;
        pay.Status = request.Status;
        pay.Notes = request.Notes;
        pay.PaidAt = request.Status == PaymentStatus.Paid ? DateTimeOffset.UtcNow : null;
        pay.PaymentSource = request.Status == PaymentStatus.Paid ? PaymentSource.Manual : PaymentSource.None;
        pay.RecordedByUserId = userId;
        pay.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        var name = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [membership.PlayerProfileId], ct))[membership.PlayerProfileId];
        return new PaymentDto(pay.Id, membershipId, name, periodId, period.Label, pay.Amount, pay.Status.ToString(), pay.PaidAt, pay.Notes, membership.PlayerProfileId, pay.PaymentSource.ToString(), null);
    }

    /// <summary>Admin do grupo da rota e mensalista pertencente a esse mesmo grupo.</summary>
    private async Task<MonthlyMembership> RequireMembershipInGroupAsync(Guid userId, Guid groupId, Guid membershipId, CancellationToken ct)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        return await _db.MonthlyMemberships.FirstOrDefaultAsync(m => m.Id == membershipId && m.GroupId == groupId, ct)
            ?? throw new DomainException("Mensalista não encontrado neste grupo.");
    }

    /// <summary>
    /// Mensalista incluído ou reativado depois que a competência do mês corrente já foi aberta
    /// recebe o lançamento pendente desse mês. Idempotente: não duplica nem altera lançamento existente.
    /// </summary>
    private async Task EnsureOpenPeriodChargeAsync(MonthlyMembership membership, Guid userId, CancellationToken ct)
    {
        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == membership.GroupId, ct);
        var today = LocalNow(group.TimeZoneId);
        var period = await _db.MembershipBillingPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.GroupId == membership.GroupId && p.Year == today.Year && p.Month == today.Month, ct);
        if (period is null) return; // competência ainda não aberta: EnsurePeriodAsync lançará para todos os ativos

        var exists = await _db.MembershipPayments.AnyAsync(
            p => p.MonthlyMembershipId == membership.Id && p.BillingPeriodId == period.Id, ct)
            || _db.MembershipPayments.Local.Any(p => p.MonthlyMembershipId == membership.Id && p.BillingPeriodId == period.Id);
        if (exists) return;

        _db.MembershipPayments.Add(new MembershipPayment
        {
            Id = Guid.NewGuid(),
            MonthlyMembershipId = membership.Id,
            BillingPeriodId = period.Id,
            Amount = GroupSettings.Parse(group.SettingsJson).DefaultMonthlyFee,
            Status = PaymentStatus.Pending,
            RecordedByUserId = userId
        });
    }

    private static DateTime LocalNow(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return DateTime.UtcNow;
        }
    }
}
