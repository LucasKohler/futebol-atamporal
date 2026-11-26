using System.Text.Json;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

public sealed class GroupSettings
{
    public int DefaultPlayerLimit { get; set; } = 14;
    public int TeamCount { get; set; } = 2;
    public decimal DefaultMonthlyFee { get; set; } = 150m;
    public decimal DefaultMatchDayFee { get; set; } = 25m;
    public int PixExpirationMinutes { get; set; } = 60;

    public string ToJson() => JsonSerializer.Serialize(this);

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

    /// <summary>
    /// Política de preservação do elenco: até o sorteio (Drawn) a lista pode mudar e o sorteio é revalidado.
    /// Depois que a partida começa (InProgress/AwaitingClose) o elenco fica congelado: ninguém sai nem é removido;
    /// quem faltar é marcado como ausente no fechamento (presença).
    /// </summary>
    public static void EnsureCanWithdraw(MatchStatus status)
    {
        if (status is MatchStatus.Completed or MatchStatus.Cancelled)
            throw new DomainException("Não é possível sair desta partida.");
        if (IsLineupFrozen(status))
            throw new DomainException("A partida já começou: o elenco está preservado. Ausências são registradas na presença do fechamento.");
    }

    /// <summary>Partida iniciada ou aguardando fechamento: elenco sorteado não muda mais.</summary>
    public static bool IsLineupFrozen(MatchStatus status)
        => status is MatchStatus.InProgress or MatchStatus.AwaitingClose;
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

    /// <summary>
    /// Ordem publicada da fila: mensalista elegível antes de avulso; depois ordem de chegada na fila.
    /// A posição exibida é a mesma ordem usada na promoção.
    /// </summary>
    public static IEnumerable<MatchRegistration> QueueOrder(IEnumerable<MatchRegistration> registrations)
        => registrations
            .Where(r => r.Status == RegistrationStatus.Waitlisted)
            .OrderByDescending(r => r.PriorityClass)
            .ThenBy(r => r.WaitlistPosition ?? int.MaxValue)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id);

    /// <summary>
    /// Promove da fila enquanto houver vaga e renumera a fila de 1 a N, sem lacunas.
    /// Opera só em memória sobre todas as inscrições da partida. Retorna os promovidos.
    /// </summary>
    public static IReadOnlyList<MatchRegistration> Rebalance(IList<MatchRegistration> registrations, int playerLimit, DateTimeOffset now)
    {
        var promoted = new List<MatchRegistration>();
        var confirmed = registrations.Count(r => r.Status == RegistrationStatus.Confirmed);
        foreach (var next in QueueOrder(registrations).ToList())
        {
            if (confirmed >= playerLimit) break;
            next.Status = RegistrationStatus.Confirmed;
            next.WaitlistPosition = null;
            next.ConfirmedAt = now;
            next.UpdatedAt = now;
            confirmed++;
            promoted.Add(next);
        }

        var position = 1;
        foreach (var waiting in QueueOrder(registrations).ToList())
        {
            if (waiting.WaitlistPosition != position)
            {
                waiting.WaitlistPosition = position;
                waiting.UpdatedAt = now;
            }
            position++;
        }

        foreach (var inactive in registrations.Where(r => r.Status != RegistrationStatus.Waitlisted && r.WaitlistPosition is not null))
            inactive.WaitlistPosition = null;

        return promoted;
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
