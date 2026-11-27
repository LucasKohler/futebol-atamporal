using System.Text.Json;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

/// <summary>
/// Ciclo da diária ligado à rodada (ver <see cref="MatchDayFeePolicy"/>). Roda dentro da transação
/// do fechamento/correção; não chama o provedor de pagamento (nenhum estorno real é disparado).
/// </summary>
internal static class MatchDayFeeLifecycle
{
    /// <summary>Lança as diárias dos presentes no fechamento da rodada/dia pelo admin.</summary>
    public static async Task LaunchOnCloseAsync(
        AppDbContext db, Match match, IReadOnlyCollection<Guid> presentProfileIds, Guid userId, DateTimeOffset closedAt, CancellationToken ct)
    {
        var group = await db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var settings = GroupSettings.Parse(group.SettingsJson);
        var dueAt = MatchDayFeePolicy.DueAt(closedAt, MatchDayFeePolicy.ResolveTimeZone(group.TimeZoneId));
        var amount = settings.DefaultMatchDayFee;

        var monthly = (await db.MonthlyMemberships.AsNoTracking()
            .Where(m => m.GroupId == match.GroupId && m.Status == MonthlyMembershipStatus.Active)
            .Select(m => m.PlayerProfileId).ToListAsync(ct)).ToHashSet();
        var existing = (await db.MatchFeePayments.Where(f => f.MatchId == match.Id).ToListAsync(ct))
            .ToDictionary(f => f.PlayerProfileId);

        var launched = new List<Guid>();
        foreach (var profileId in presentProfileIds.Distinct())
        {
            if (existing.TryGetValue(profileId, out var fee))
            {
                // Já existia (pagamento antecipado ou lançamento manual): mantém valor e situação.
                if (fee.Status == PaymentStatus.Pending && fee.DueAt is null)
                {
                    fee.DueAt = dueAt;
                    fee.UpdatedAt = closedAt;
                }
                continue;
            }

            if (!MatchDayFeePolicy.ShouldLaunch(true, monthly.Contains(profileId), amount))
                continue;

            db.MatchFeePayments.Add(new MatchFeePayment
            {
                Id = Guid.NewGuid(),
                GroupId = match.GroupId,
                MatchId = match.Id,
                PlayerProfileId = profileId,
                Amount = amount,
                Status = PaymentStatus.Pending,
                PaymentSource = PaymentSource.None,
                DueAt = dueAt,
                RecordedByUserId = userId,
                CreatedAt = closedAt,
                UpdatedAt = closedAt
            });
            launched.Add(profileId);
        }

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = match.GroupId,
            ActorUserId = userId,
            Action = "match_fee.launched",
            EntityType = nameof(Match),
            EntityId = match.Id,
            PayloadJson = JsonSerializer.Serialize(new { count = launched.Count, amount, dueAt, players = launched })
        });
    }

    /// <summary>
    /// Correção de presença: presente → ausente remove a diária não paga (Cancelled) e marca a paga
    /// para devolução (RefundPending). Ausente → presente não gera cobrança (regra ainda não definida).
    /// </summary>
    public static async Task ApplyPresenceCorrectionAsync(
        AppDbContext db, Match match, RoundSheet previous, RoundSheet next, Guid userId, string reason, CancellationToken ct)
    {
        var wasPresent = previous.Participants.Where(p => p.Present).Select(p => p.PlayerProfileId).ToHashSet();
        var nowAbsent = next.Participants.Where(p => !p.Present && wasPresent.Contains(p.PlayerProfileId))
            .Select(p => p.PlayerProfileId).ToList();
        if (nowAbsent.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var fees = await db.MatchFeePayments
            .Where(f => f.MatchId == match.Id && nowAbsent.Contains(f.PlayerProfileId)).ToListAsync(ct);

        foreach (var fee in fees)
        {
            var effective = MatchDayFeePolicy.EffectiveStatus(fee.Status, fee.DueAt, now);
            if (MatchDayFeePolicy.OnPresenceRemoved(effective) is not { } target)
                continue;

            var from = fee.Status;
            fee.Status = target;
            fee.UpdatedAt = now;

            if (target == PaymentStatus.Cancelled)
            {
                // Pix em aberto deixa de valer; aprovação tardia é recusada e auditada pela baixa Pix.
                var open = await db.PixCharges.Where(c => c.MatchFeePaymentId == fee.Id
                    && (c.Status == PixChargeStatus.Pending || c.Status == PixChargeStatus.Expired)).ToListAsync(ct);
                foreach (var charge in open)
                {
                    charge.Status = PixChargeStatus.Cancelled;
                    charge.UpdatedAt = now;
                }
            }

            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                GroupId = match.GroupId,
                ActorUserId = userId,
                Action = target == PaymentStatus.Cancelled ? "match_fee.removed_absent" : "match_fee.refund_requested",
                EntityType = nameof(MatchFeePayment),
                EntityId = fee.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    matchId = match.Id,
                    playerProfileId = fee.PlayerProfileId,
                    from = from.ToString(),
                    to = target.ToString(),
                    amount = fee.Amount,
                    paymentSource = fee.PaymentSource.ToString(),
                    reason
                })
            });
        }
    }
}
