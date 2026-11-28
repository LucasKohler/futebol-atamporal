using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

internal static class RegistrationEligibility
{
    public static async Task<bool> IsMonthlyAsync(
        AppDbContext db, Guid groupId, Guid playerProfileId, DateTimeOffset now, CancellationToken ct)
    {
        var membership = await db.MonthlyMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.PlayerProfileId == playerProfileId
                                      && m.Status == MonthlyMembershipStatus.Active, ct);
        if (membership is null) return false;

        var period = await db.MembershipBillingPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.GroupId == groupId && p.Year == now.Year && p.Month == now.Month, ct);
        if (period is null) return true;

        var payment = await db.MembershipPayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MonthlyMembershipId == membership.Id && p.BillingPeriodId == period.Id, ct);
        return payment is null || payment.Status is PaymentStatus.Paid or PaymentStatus.Exempt or PaymentStatus.Pending;
    }
}
