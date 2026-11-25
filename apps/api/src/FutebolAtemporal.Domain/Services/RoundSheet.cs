using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

/// <summary>Quem estava escalado, em qual time, se compareceu e se atuou no gol (PO-1).</summary>
public sealed record RoundParticipant(Guid PlayerProfileId, Guid TeamId, bool Present, bool PlayedGoalkeeper);

/// <summary>Confronto entre duas cores. Ordem começa em 1.</summary>
public sealed record RoundGame(int Order, Guid HomeTeamId, Guid AwayTeamId, int HomeScore, int AwayScore);

/// <summary>Gol de um jogador num confronto. Gol contra conta para o adversário e fica fora da artilharia.</summary>
public sealed record RoundGoal(int GameOrder, Guid PlayerProfileId, bool OwnGoal);

/// <summary>Súmula completa da rodada de três cores; cada correção gera uma nova revisão desta súmula.</summary>
public sealed record RoundSheet(
    IReadOnlyList<RoundParticipant> Participants,
    IReadOnlyList<RoundGame> Games,
    IReadOnlyList<RoundGoal> Goals,
    Guid? BestPlayerId,
    Guid? BestGoalkeeperId,
    string? Notes);

public sealed record TeamStanding(
    Guid TeamId,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    int Points,
    bool IsRoundWinner);

/// <summary>Resultado individual numa rodada (só a revisão vigente entra nas contas).</summary>
public sealed record PlayerRoundLine(
    Guid PlayerProfileId,
    Guid TeamId,
    bool Present,
    bool PlayedGoalkeeper,
    int Goals,
    int OwnGoals,
    int GamesWon,
    bool RoundWon,
    bool BestPlayer,
    bool BestGoalkeeper);

/// <summary>
/// Regras do pós-jogo de três cores (PO-1).
/// Vitória/empate derivam do placar; gols atribuídos + gols contra fecham o placar de cada lado;
/// gols e prêmios exigem presença; Melhor Goleiro exige ter atuado no gol.
/// Vencedor da rodada (provisório até decisão do Lucas): mais pontos (3 vitória, 1 empate), depois saldo,
/// depois gols pró; empate persistente divide a vitória da rodada.
/// </summary>
public static class RoundRules
{
    public const int MaxScore = 99;
    public const int MaxGames = 30;
    public const int MinReasonLength = 5;

    public static void Validate(RoundSheet sheet, IReadOnlyCollection<Guid> teamIds)
    {
        if (teamIds.Count < 2)
            throw new DomainException("A rodada precisa de ao menos dois times.");
        if (sheet.Participants.Count == 0)
            throw new DomainException("Informe a presença dos jogadores escalados.");
        if (sheet.Participants.Select(p => p.PlayerProfileId).Distinct().Count() != sheet.Participants.Count)
            throw new DomainException("Jogador repetido na presença.");
        if (sheet.Participants.Any(p => !teamIds.Contains(p.TeamId)))
            throw new DomainException("Jogador vinculado a um time que não pertence ao sorteio.");
        if (sheet.Games.Count == 0)
            throw new DomainException("Registre ao menos um confronto da rodada.");
        if (sheet.Games.Count > MaxGames)
            throw new DomainException($"No máximo {MaxGames} confrontos por rodada.");

        var orders = sheet.Games.Select(g => g.Order).ToList();
        if (!orders.Order().SequenceEqual(Enumerable.Range(1, sheet.Games.Count)))
            throw new DomainException("Os confrontos devem ser numerados em sequência a partir de 1.");

        foreach (var game in sheet.Games)
        {
            if (game.HomeTeamId == game.AwayTeamId)
                throw new DomainException($"Confronto {game.Order}: escolha duas cores diferentes.");
            if (!teamIds.Contains(game.HomeTeamId) || !teamIds.Contains(game.AwayTeamId))
                throw new DomainException($"Confronto {game.Order}: time fora do sorteio da rodada.");
            if (game.HomeScore is < 0 or > MaxScore || game.AwayScore is < 0 or > MaxScore)
                throw new DomainException($"Confronto {game.Order}: placar deve ficar entre 0 e {MaxScore}.");
        }

        var byPlayer = sheet.Participants.ToDictionary(p => p.PlayerProfileId);
        foreach (var goal in sheet.Goals)
        {
            var game = sheet.Games.FirstOrDefault(g => g.Order == goal.GameOrder)
                ?? throw new DomainException($"Gol lançado no confronto {goal.GameOrder}, que não existe.");
            if (!byPlayer.TryGetValue(goal.PlayerProfileId, out var scorer))
                throw new DomainException("Gol atribuído a quem não está escalado na rodada.");
            if (!scorer.Present)
                throw new DomainException("Gol atribuído a jogador ausente.");
            if (scorer.TeamId != game.HomeTeamId && scorer.TeamId != game.AwayTeamId)
                throw new DomainException($"Confronto {game.Order}: o autor do gol não joga em nenhum dos dois times.");
        }

        foreach (var game in sheet.Games)
        {
            var (home, away) = CreditedGoals(sheet, game, byPlayer);
            var hasGoals = sheet.Goals.Any(g => g.GameOrder == game.Order);
            if (hasGoals && (home != game.HomeScore || away != game.AwayScore))
                throw new DomainException(
                    $"Confronto {game.Order}: os gols lançados ({home} x {away}) não fecham o placar ({game.HomeScore} x {game.AwayScore}).");
            if (!hasGoals && game.HomeScore + game.AwayScore > 0)
                throw new DomainException($"Confronto {game.Order}: lance os autores dos {game.HomeScore + game.AwayScore} gols (ou gol contra).");
        }

        if (sheet.BestPlayerId is { } best)
        {
            if (!byPlayer.TryGetValue(best, out var p) || !p.Present)
                throw new DomainException("Melhor Jogador deve estar entre os presentes.");
        }
        if (sheet.BestGoalkeeperId is { } keeper)
        {
            if (!byPlayer.TryGetValue(keeper, out var p) || !p.Present)
                throw new DomainException("Melhor Goleiro deve estar entre os presentes.");
            if (!p.PlayedGoalkeeper)
                throw new DomainException("Melhor Goleiro deve ter atuado no gol nesta rodada.");
        }
        if (sheet.Notes is { Length: > 1000 })
            throw new DomainException("Observações devem ter no máximo 1000 caracteres.");
    }

    public static void EnsureReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinReasonLength)
            throw new DomainException($"Informe o motivo da correção (mínimo {MinReasonLength} caracteres).");
        if (reason.Length > 500)
            throw new DomainException("O motivo deve ter no máximo 500 caracteres.");
    }

    /// <summary>Gols por lado: gols normais do próprio time + gols contra do adversário.</summary>
    private static (int Home, int Away) CreditedGoals(RoundSheet sheet, RoundGame game, IReadOnlyDictionary<Guid, RoundParticipant> byPlayer)
    {
        int home = 0, away = 0;
        foreach (var goal in sheet.Goals.Where(g => g.GameOrder == game.Order))
        {
            var team = byPlayer[goal.PlayerProfileId].TeamId;
            var creditedToHome = goal.OwnGoal ? team == game.AwayTeamId : team == game.HomeTeamId;
            if (creditedToHome) home++; else away++;
        }
        return (home, away);
    }

    public static IReadOnlyList<TeamStanding> Standings(RoundSheet sheet, IReadOnlyCollection<Guid> teamIds)
    {
        var rows = teamIds.ToDictionary(t => t, _ => (P: 0, W: 0, D: 0, L: 0, GF: 0, GA: 0));
        foreach (var g in sheet.Games)
        {
            var h = rows[g.HomeTeamId];
            var a = rows[g.AwayTeamId];
            h.P++; a.P++;
            h.GF += g.HomeScore; h.GA += g.AwayScore;
            a.GF += g.AwayScore; a.GA += g.HomeScore;
            if (g.HomeScore > g.AwayScore) { h.W++; a.L++; }
            else if (g.HomeScore < g.AwayScore) { a.W++; h.L++; }
            else { h.D++; a.D++; }
            rows[g.HomeTeamId] = h;
            rows[g.AwayTeamId] = a;
        }

        var scored = rows.Select(r => (Team: r.Key, r.Value, Points: r.Value.W * 3 + r.Value.D)).ToList();
        var best = scored
            .OrderByDescending(s => s.Points).ThenByDescending(s => s.Value.GF - s.Value.GA).ThenByDescending(s => s.Value.GF)
            .First();
        bool Ties(int pts, int gd, int gf) => pts == best.Points && gd == best.Value.GF - best.Value.GA && gf == best.Value.GF;

        return scored.Select(s => new TeamStanding(
            s.Team, s.Value.P, s.Value.W, s.Value.D, s.Value.L, s.Value.GF, s.Value.GA, s.Points,
            s.Value.P > 0 && Ties(s.Points, s.Value.GF - s.Value.GA, s.Value.GF))).ToList();
    }

    /// <summary>Linhas individuais da rodada: gols (sem gol contra), vitórias do time enquanto presente e prêmios.</summary>
    public static IReadOnlyList<PlayerRoundLine> PlayerLines(RoundSheet sheet, IReadOnlyCollection<Guid> teamIds)
    {
        var standings = Standings(sheet, teamIds).ToDictionary(s => s.TeamId);
        return sheet.Participants.Select(p => new PlayerRoundLine(
            p.PlayerProfileId,
            p.TeamId,
            p.Present,
            p.PlayedGoalkeeper,
            p.Present ? sheet.Goals.Count(g => g.PlayerProfileId == p.PlayerProfileId && !g.OwnGoal) : 0,
            p.Present ? sheet.Goals.Count(g => g.PlayerProfileId == p.PlayerProfileId && g.OwnGoal) : 0,
            p.Present ? standings[p.TeamId].Wins : 0,
            p.Present && standings[p.TeamId].IsRoundWinner,
            sheet.BestPlayerId == p.PlayerProfileId,
            sheet.BestGoalkeeperId == p.PlayerProfileId)).ToList();
    }
}

public sealed record ScorerRow(Guid PlayerProfileId, int Goals, int Position, bool IsLeader);

/// <summary>
/// Tabela de artilharia: ordem por gols; empate em gols divide a posição (1, 1, 3...) e a liderança.
/// Só entra quem marcou ao menos um gol; gol contra nunca entra.
/// </summary>
public static class ScorerTable
{
    public static IReadOnlyList<ScorerRow> Rank(IEnumerable<(Guid PlayerProfileId, int Goals)> goals)
    {
        var rows = goals
            .GroupBy(g => g.PlayerProfileId)
            .Select(g => (Id: g.Key, Goals: g.Sum(x => x.Goals)))
            .Where(r => r.Goals > 0)
            .OrderByDescending(r => r.Goals).ThenBy(r => r.Id)
            .ToList();
        if (rows.Count == 0) return [];

        var top = rows[0].Goals;
        var result = new List<ScorerRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var position = i > 0 && rows[i].Goals == rows[i - 1].Goals ? result[i - 1].Position : i + 1;
            result.Add(new ScorerRow(rows[i].Id, rows[i].Goals, position, rows[i].Goals == top));
        }
        return result;
    }
}
