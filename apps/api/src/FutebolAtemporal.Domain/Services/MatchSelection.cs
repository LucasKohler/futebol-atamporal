using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Domain.Services;

/// <summary>Seleção canônica de partidas, compartilhada por /api/matches/upcoming e pelos lembretes.</summary>
public static class MatchSelection
{
    /// <summary>
    /// Próxima partida: a mais cedo não encerrada/cancelada que começa a partir de 6 h atrás;
    /// sem ela, uma partida com lista aberta (mais antiga primeiro, por Id para desempate estável).
    /// </summary>
    public static Match? Upcoming(IEnumerable<Match> matches, DateTimeOffset now)
    {
        var candidates = matches.Where(m => m.Status != MatchStatus.Cancelled && m.Status != MatchStatus.Completed).ToList();
        return candidates
            .Where(m => m.StartsAt >= now.AddHours(-6))
            .OrderBy(m => m.StartsAt)
            .FirstOrDefault()
            ?? candidates.Where(m => m.Status == MatchStatus.RegistrationOpen).OrderBy(m => m.Id).FirstOrDefault();
    }

    /// <summary>Última rodada fechada (é no fechamento que a diária nasce).</summary>
    public static Match? LatestClosed(IEnumerable<Match> matches)
        => matches.Where(m => m.Status == MatchStatus.Completed).OrderByDescending(m => m.StartsAt).FirstOrDefault();
}
