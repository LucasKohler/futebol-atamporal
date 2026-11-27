using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Domain.Services;

/// <summary>
/// Regras confirmadas da diária (Lucas, 27/11):
/// - nasce quando o admin fecha a rodada/dia inteiro, com valor congelado no lançamento;
/// - vence no fim do mesmo dia, no fuso do grupo; a partir daí fica atrasada;
/// - se a correção muda a presença de presente para ausente, a diária não paga é removida
///   e a paga deve ser devolvida (reembolso, não crédito).
/// Multa, bloqueio por ausência, rodada cancelada e fechamento após meia-noite não estão aqui.
/// </summary>
public static class MatchDayFeePolicy
{
    /// <summary>Situação exibida para partida ainda não fechada: não há cobrança nem checkout.</summary>
    public const string NotChargedStatus = "NotCharged";

    /// <summary>Não existe pagamento antes do fechamento: cobrança e checkout só com a rodada encerrada.</summary>
    public static bool IsChargeOpen(MatchStatus status) => status == MatchStatus.Completed;

    public static void EnsureChargeOpen(MatchStatus status)
    {
        if (!IsChargeOpen(status))
            throw new Exceptions.ConflictException(
                "A diária só é cobrada depois que o admin fecha a rodada.", "match_fee_not_open");
    }

    /// <summary>
    /// Instante (exclusivo) em que a diária passa a estar atrasada: a meia-noite local seguinte
    /// ao fechamento, no fuso do grupo. Se a meia-noite não existir (horário de verão), usa o
    /// primeiro horário válido depois dela.
    /// </summary>
    public static DateTimeOffset DueAt(DateTimeOffset closedAt, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(closedAt, timeZone);
        var nextMidnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(nextMidnight))
            nextMidnight = nextMidnight.AddMinutes(15);
        return new DateTimeOffset(nextMidnight, timeZone.GetUtcOffset(nextMidnight));
    }

    /// <summary>Resolve o fuso do grupo; fuso inválido cai em UTC, como no restante da API.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>Situação exibida: pendente com vencimento passado vira "Overdue" sem depender de job.</summary>
    public static PaymentStatus EffectiveStatus(PaymentStatus stored, DateTimeOffset? dueAt, DateTimeOffset now)
        => stored == PaymentStatus.Pending && dueAt is { } due && now >= due ? PaymentStatus.Overdue : stored;

    /// <summary>Diária só é lançada para quem esteve presente, não é mensalista ativo e com valor positivo.</summary>
    public static bool ShouldLaunch(bool present, bool isActiveMonthly, decimal amount)
        => present && !isActiveMonthly && amount > 0;

    /// <summary>
    /// Efeito da correção de presença presente → ausente sobre a diária.
    /// Null = nada muda (já isenta, cancelada ou em reembolso).
    /// </summary>
    public static PaymentStatus? OnPresenceRemoved(PaymentStatus current) => current switch
    {
        PaymentStatus.Pending or PaymentStatus.Overdue => PaymentStatus.Cancelled,
        PaymentStatus.Paid => PaymentStatus.RefundPending,
        _ => null
    };

    /// <summary>
    /// Transições manuais do admin envolvendo reembolso: RefundPending só nasce pela correção de presença;
    /// de RefundPending só se vai para Refunded (devolução confirmada); Refunded é final.
    /// </summary>
    public static bool IsAllowedManualTransition(PaymentStatus from, PaymentStatus to)
    {
        if (to == PaymentStatus.RefundPending) return from == PaymentStatus.RefundPending;
        if (from == PaymentStatus.RefundPending) return to is PaymentStatus.RefundPending or PaymentStatus.Refunded;
        if (from == PaymentStatus.Refunded) return to == PaymentStatus.Refunded;
        if (to == PaymentStatus.Refunded) return false;
        return true;
    }
}
