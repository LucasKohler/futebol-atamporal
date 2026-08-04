using System.Text.Json;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

public sealed class GroupSettings
{
    public int DefaultPlayerLimit { get; set; } = 14;
    public int TeamCount { get; set; } = 2;
    public decimal DefaultMonthlyFee { get; set; } = 150m;

    public static GroupSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new GroupSettings();
        try
        {
            return JsonSerializer.Deserialize<GroupSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new GroupSettings();
        }
        catch
        {
            return new GroupSettings();
        }
    }
}

public static class RegistrationPolicy
{
    public static void EnsureCanRegister(MatchStatus status)
    {
        if (status != MatchStatus.RegistrationOpen)
            throw new DomainException("Inscrições não estão abertas para esta partida.");
    }

    public static void EnsureCanWithdraw(MatchStatus status)
    {
        if (status is MatchStatus.Completed or MatchStatus.Cancelled)
            throw new DomainException("Não é possível sair desta partida.");
    }
}

/// <summary>FIFO por capacidade. Mensalistas só afetam ordem na promoção da fila.</summary>
public static class WaitlistLogic
{
    public static (RegistrationStatus Status, int? WaitlistPosition) Place(int confirmedCount, int playerLimit, int currentWaitlistCount)
    {
        if (confirmedCount < playerLimit)
            return (RegistrationStatus.Confirmed, null);
        return (RegistrationStatus.Waitlisted, currentWaitlistCount + 1);
    }
}

public static class MatchTransitions
{
    private static readonly Dictionary<MatchStatus, HashSet<MatchStatus>> Allowed = new()
    {
        [MatchStatus.Draft] = [MatchStatus.RegistrationOpen, MatchStatus.Cancelled],
        [MatchStatus.RegistrationOpen] = [MatchStatus.RegistrationClosed, MatchStatus.Cancelled],
        [MatchStatus.RegistrationClosed] = [MatchStatus.Drawn, MatchStatus.RegistrationOpen, MatchStatus.Completed, MatchStatus.Cancelled],
        [MatchStatus.Drawn] = [MatchStatus.InProgress, MatchStatus.RegistrationClosed, MatchStatus.Completed, MatchStatus.Cancelled],
        [MatchStatus.InProgress] = [MatchStatus.AwaitingClose, MatchStatus.Completed, MatchStatus.Cancelled],
        [MatchStatus.AwaitingClose] = [MatchStatus.Completed],
        [MatchStatus.Completed] = [],
        [MatchStatus.Cancelled] = []
    };

    public static void EnsureTransition(MatchStatus from, MatchStatus to)
    {
        if (!Allowed.TryGetValue(from, out var set) || !set.Contains(to))
            throw new DomainException($"Transição de {from} para {to} não é permitida.");
    }

    public static bool CanClose(MatchStatus status)
        => status is MatchStatus.RegistrationClosed or MatchStatus.Drawn or MatchStatus.InProgress or MatchStatus.AwaitingClose;
}
