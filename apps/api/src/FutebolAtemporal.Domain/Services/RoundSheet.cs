using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

/// <summary>Quem estava escalado, em qual time, se compareceu e se atuou no gol (PO-1).</summary>
public sealed record RoundParticipant(Guid PlayerProfileId, Guid TeamId, bool Present, bool PlayedGoalkeeper);

/// <summary>Defesas de pênalti de um goleiro em um confronto (1..10).</summary>
public sealed record RoundPenaltySave(Guid GoalkeeperProfileId, int Count);

/// <summary>
/// Confronto entre duas cores. Ordem começa em 1.
/// Campos novos têm valores padrão para manter leitura retrocompatível de súmulas v1 salvas em JSON.
/// </summary>
public sealed record RoundGame(
    int Order,
    Guid HomeTeamId,
    Guid AwayTeamId,
    int HomeScore,
    int AwayScore,
    Guid Id = default,
    RoundGameStatus Status = RoundGameStatus.Finished,
    Guid? PenaltyWinnerTeamId = null,
    IReadOnlyList<RoundPenaltySave>? PenaltySaves = null)
{
    public bool DecidedByPenalties =>
        Status == RoundGameStatus.Finished && HomeScore == AwayScore && PenaltyWinnerTeamId.HasValue;

    public Guid? WinnerTeamId =>
        Status != RoundGameStatus.Finished
            ? null
            : HomeScore > AwayScore
                ? HomeTeamId
                : AwayScore > HomeScore
                    ? AwayTeamId
                    : PenaltyWinnerTeamId;
}

/// <summary>Gol de um jogador num confronto. Gol contra conta para o adversário e fica fora da artilharia.</summary>
public sealed record RoundGoal(int GameOrder, Guid PlayerProfileId, bool OwnGoal, Guid Id = default);

/// <summary>
/// Súmula completa da rodada de três cores; cada correção gera uma nova revisão desta súmula.
/// RulesVersion = 1 (legado com empate 3/1) | 2 (opção A: pênaltis alternados + defesas de pênalti).
/// </summary>
public sealed record RoundSheet(
    IReadOnlyList<RoundParticipant> Participants,
    IReadOnlyList<RoundGame> Games,
    IReadOnlyList<RoundGoal> Goals,
    Guid? BestPlayerId,
    Guid? BestGoalkeeperId,
    string? Notes,
    int RulesVersion = 1);

public sealed record TeamStanding(
    Guid TeamId,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    int Points,
    bool IsRoundWinner,
    int WinsRegular = 0,
    int WinsPenalties = 0);

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
    bool BestGoalkeeper,
    int GamesWonOnPenalties = 0,
    int PenaltySaves = 0);

/// <summary>
/// Regras do pós-jogo e modo ao vivo de três cores (D1–D6, Opção A).
/// v1 (legado): empate gera 1 ponto (3/1).
/// v2 (atual): empate em jogo Finished exige PenaltyWinnerTeamId; vitória normal ou nos pênaltis vale 1 vitória;
/// defesas de pênalti vinculadas a goleiro presente de uma das cores do confronto.
/// </summary>
public static class RoundRules
{
    public const int CurrentRulesVersion = 2;
    public const int MaxScore = 99;
    public const int MaxGames = 30;
    public const int MaxPenaltySavesPerGame = 10;
    public const int MinReasonLength = 5;

    public static void Validate(RoundSheet sheet, IReadOnlyCollection<Guid> teamIds)
        => ValidateInternal(sheet, teamIds, allowEmptyGames: false, allowInProgressGames: false);

    public static void ValidateLive(RoundSheet sheet, IReadOnlyCollection<Guid> teamIds)
        => ValidateInternal(sheet, teamIds, allowEmptyGames: true, allowInProgressGames: true);

    private static void ValidateInternal(
        RoundSheet sheet,
        IReadOnlyCollection<Guid> teamIds,
        bool allowEmptyGames,
        bool allowInProgressGames)
    {
        if (teamIds.Count < 2)
            throw new DomainException("A rodada precisa de ao menos dois times.", "invalid_game");
        if (sheet.Participants.Count == 0)
            throw new DomainException("Informe a presença dos jogadores escalados.", "invalid_game");
        if (sheet.Participants.Select(p => p.PlayerProfileId).Distinct().Count() != sheet.Participants.Count)
            throw new DomainException("Jogador repetido na presença.", "invalid_game");
        if (sheet.Participants.Any(p => !teamIds.Contains(p.TeamId)))
            throw new DomainException("Jogador vinculado a um time que não pertence ao sorteio.", "invalid_game");
        if (!allowEmptyGames && sheet.Games.Count == 0)
            throw new DomainException("Registre ao menos um confronto da rodada.", "invalid_game");
        if (sheet.Games.Count > MaxGames)
            throw new DomainException($"No máximo {MaxGames} confrontos por rodada.", "invalid_game");

        if (sheet.Games.Count > 0)
        {
            var orders = sheet.Games.Select(g => g.Order).ToList();
            if (!orders.Order().SequenceEqual(Enumerable.Range(1, sheet.Games.Count)))
                throw new DomainException("Os confrontos devem ser numerados em sequência a partir de 1.", "invalid_game");
        }

        var inProgressCount = sheet.Games.Count(g => g.Status == RoundGameStatus.InProgress);
        if (!allowInProgressGames && inProgressCount > 0)
            throw new ConflictException("Finalize a partida em andamento antes de encerrar o dia.", "game_in_progress");
        if (allowInProgressGames && inProgressCount > 1)
            throw new ConflictException("Já existe uma partida em andamento neste dia.", "game_in_progress");

        var byPlayer = sheet.Participants.ToDictionary(p => p.PlayerProfileId);
        var isV2 = sheet.RulesVersion >= 2;

        foreach (var game in sheet.Games)
        {
            if (game.HomeTeamId == game.AwayTeamId)
                throw new DomainException($"Confronto {game.Order}: escolha duas cores diferentes.", "invalid_game");
            if (!teamIds.Contains(game.HomeTeamId) || !teamIds.Contains(game.AwayTeamId))
                throw new DomainException($"Confronto {game.Order}: time fora do sorteio da rodada.", "invalid_game");
            if (game.HomeScore is < 0 or > MaxScore || game.AwayScore is < 0 or > MaxScore)
                throw new DomainException($"Confronto {game.Order}: placar deve ficar entre 0 e {MaxScore}.", "invalid_game");

            if (isV2)
            {
                if (game.Status == RoundGameStatus.InProgress)
                {
                    if (game.PenaltyWinnerTeamId is not null)
                        throw new DomainException($"Confronto {game.Order}: vencedor nos pênaltis só pode ser informado ao encerrar a partida.", "invalid_game");
                }
                else
                {
                    if (game.HomeScore == game.AwayScore)
                    {
                        if (game.PenaltyWinnerTeamId is null)
                            throw new DomainException($"Confronto {game.Order}: empate exige informar quem venceu nos pênaltis.", "invalid_game");
                        if (game.PenaltyWinnerTeamId != game.HomeTeamId && game.PenaltyWinnerTeamId != game.AwayTeamId)
                            throw new DomainException($"Confronto {game.Order}: vencedor nos pênaltis deve ser uma das duas cores em campo.", "invalid_game");
                    }
                    else if (game.PenaltyWinnerTeamId is not null)
                    {
                        throw new DomainException($"Confronto {game.Order}: vencedor nos pênaltis só se aplica quando o tempo normal termina empatado.", "invalid_game");
                    }
                }
            }

            if (game.PenaltySaves is { Count: > 0 } saves)
            {
                if (saves.Select(s => s.GoalkeeperProfileId).Distinct().Count() != saves.Count)
                    throw new DomainException($"Confronto {game.Order}: goleiro repetido nas defesas de pênalti.", "invalid_game");
                foreach (var save in saves)
                {
                    if (save.Count is < 1 or > MaxPenaltySavesPerGame)
                        throw new DomainException($"Confronto {game.Order}: defesas de pênalti devem ficar entre 1 e {MaxPenaltySavesPerGame}.", "invalid_game");
                    if (!byPlayer.TryGetValue(save.GoalkeeperProfileId, out var keeper))
                        throw new DomainException($"Confronto {game.Order}: defesa de pênalti atribuída a quem não está no sorteio.", "invalid_game");
                    if (!keeper.Present)
                        throw new DomainException($"Confronto {game.Order}: defesa de pênalti atribuída a jogador ausente.", "invalid_game");
                    if (!keeper.PlayedGoalkeeper)
                        throw new DomainException($"Confronto {game.Order}: defesa de pênalti exige jogador marcado como goleiro no dia.", "invalid_game");
                    if (keeper.TeamId != game.HomeTeamId && keeper.TeamId != game.AwayTeamId)
                        throw new DomainException($"Confronto {game.Order}: o goleiro da defesa não pertence a nenhuma das duas cores do jogo.", "invalid_game");
                }
            }
        }

        foreach (var goal in sheet.Goals)
        {
            var game = sheet.Games.FirstOrDefault(g => g.Order == goal.GameOrder)
                ?? throw new DomainException($"Gol lançado no confronto {goal.GameOrder}, que não existe.", "invalid_game");
            if (!byPlayer.TryGetValue(goal.PlayerProfileId, out var scorer))
                throw new DomainException("Gol atribuído a quem não está escalado na rodada.", "invalid_game");
            if (!scorer.Present)
                throw new DomainException("Gol atribuído a jogador ausente.", "invalid_game");
            if (scorer.TeamId != game.HomeTeamId && scorer.TeamId != game.AwayTeamId)
                throw new DomainException($"Confronto {game.Order}: o autor do gol não joga em nenhum dos dois times.", "invalid_game");
        }

        foreach (var game in sheet.Games)
        {
            var (home, away) = CreditedGoals(sheet, game, byPlayer);
            var hasGoals = sheet.Goals.Any(g => g.GameOrder == game.Order);
            if (hasGoals && (home != game.HomeScore || away != game.AwayScore))
                throw new DomainException(
                    $"Confronto {game.Order}: os gols lançados ({home} x {away}) não fecham o placar ({game.HomeScore} x {game.AwayScore}).",
                    "invalid_game");
            if (!hasGoals && game.HomeScore + game.AwayScore > 0)
                throw new DomainException(
                    $"Confronto {game.Order}: lance os autores dos {game.HomeScore + game.AwayScore} gols (ou gol contra).",
                    "invalid_game");
        }

        if (sheet.BestPlayerId is { } best)
        {
            if (!byPlayer.TryGetValue(best, out var p) || !p.Present)
                throw new DomainException("Melhor Jogador deve estar entre os presentes.", "invalid_game");
        }
        if (sheet.BestGoalkeeperId is { } bestKeeper)
        {
            if (!byPlayer.TryGetValue(bestKeeper, out var p) || !p.Present)
                throw new DomainException("Melhor Goleiro deve estar entre os presentes.", "invalid_game");
            if (!p.PlayedGoalkeeper)
                throw new DomainException("Melhor Goleiro deve ter atuado no gol nesta rodada.", "invalid_game");
        }
        if (sheet.Notes is { Length: > 1000 })
            throw new DomainException("Observações devem ter no máximo 1000 caracteres.", "invalid_game");
    }

    public static void EnsureReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinReasonLength)
            throw new DomainException($"Informe o motivo da correção (mínimo {MinReasonLength} caracteres).", "invalid_game");
        if (reason.Length > 500)
            throw new DomainException("O motivo deve ter no máximo 500 caracteres.", "invalid_game");
    }

    public static Guid CreditedTeamId(RoundGame game, RoundGoal goal, IReadOnlyDictionary<Guid, RoundParticipant> byPlayer)
    {
        var team = byPlayer[goal.PlayerProfileId].TeamId;
        var forHome = goal.OwnGoal ? team == game.AwayTeamId : team == game.HomeTeamId;
        return forHome ? game.HomeTeamId : game.AwayTeamId;
    }

    public static (int Home, int Away) ComputeScores(
        RoundGame game,
        IEnumerable<RoundGoal> goals,
        IReadOnlyDictionary<Guid, RoundParticipant> byPlayer)
    {
        int home = 0, away = 0;
        foreach (var goal in goals.Where(g => g.GameOrder == game.Order))
        {
            var team = byPlayer[goal.PlayerProfileId].TeamId;
            var forHome = goal.OwnGoal ? team == game.AwayTeamId : team == game.HomeTeamId;
            if (forHome) home++; else away++;
        }
        return (home, away);
    }

    private static (int Home, int Away) CreditedGoals(RoundSheet sheet, RoundGame game, IReadOnlyDictionary<Guid, RoundParticipant> byPlayer)
        => ComputeScores(game, sheet.Goals, byPlayer);

    /// <summary>
    /// Classificação dos times na rodada (apenas jogos Finished).
    /// v1: 3 pontos vitória, 1 empate; desempate pontos -> saldo -> gols pró.
    /// v2 (Opção A): vitória normal ou nos pênaltis vale 1 vitória (Draws=0, Points=Wins);
    /// desempate vitórias -> vitórias no tempo normal -> saldo -> gols pró.
    /// </summary>
    public static IReadOnlyList<TeamStanding> Standings(RoundSheet sheet, IReadOnlyCollection<Guid> teamIds)
    {
        var finishedGames = sheet.Games.Where(g => g.Status == RoundGameStatus.Finished).ToList();

        if (sheet.RulesVersion < 2)
        {
            var rowsV1 = teamIds.ToDictionary(id => id, _ => (P: 0, W: 0, D: 0, L: 0, GF: 0, GA: 0));
            foreach (var g in finishedGames)
            {
                var h = rowsV1[g.HomeTeamId];
                var a = rowsV1[g.AwayTeamId];
                h.P++; a.P++;
                h.GF += g.HomeScore; h.GA += g.AwayScore;
                a.GF += g.AwayScore; a.GA += g.HomeScore;
                if (g.HomeScore > g.AwayScore) { h.W++; a.L++; }
                else if (g.HomeScore < g.AwayScore) { a.W++; h.L++; }
                else { h.D++; a.D++; }
                rowsV1[g.HomeTeamId] = h;
                rowsV1[g.AwayTeamId] = a;
            }

            var scoredV1 = rowsV1.Select(r => (Team: r.Key, r.Value, Points: r.Value.W * 3 + r.Value.D)).ToList();
            var bestV1 = scoredV1
                .OrderByDescending(s => s.Points).ThenByDescending(s => s.Value.GF - s.Value.GA).ThenByDescending(s => s.Value.GF)
                .First();
            bool TiesV1(int pts, int gd, int gf) => pts == bestV1.Points && gd == bestV1.Value.GF - bestV1.Value.GA && gf == bestV1.Value.GF;

            return scoredV1.Select(s => new TeamStanding(
                s.Team, s.Value.P, s.Value.W, s.Value.D, s.Value.L, s.Value.GF, s.Value.GA, s.Points,
                s.Value.P > 0 && TiesV1(s.Points, s.Value.GF - s.Value.GA, s.Value.GF),
                WinsRegular: s.Value.W,
                WinsPenalties: 0)).ToList();
        }

        var rows = teamIds.ToDictionary(id => id, _ => (P: 0, WReg: 0, WPen: 0, L: 0, GF: 0, GA: 0));
        foreach (var g in finishedGames)
        {
            var h = rows[g.HomeTeamId];
            var a = rows[g.AwayTeamId];
            h.P++; a.P++;
            h.GF += g.HomeScore; h.GA += g.AwayScore;
            a.GF += g.AwayScore; a.GA += g.HomeScore;
            if (g.HomeScore > g.AwayScore) { h.WReg++; a.L++; }
            else if (g.HomeScore < g.AwayScore) { a.WReg++; h.L++; }
            else if (g.PenaltyWinnerTeamId == g.HomeTeamId) { h.WPen++; a.L++; }
            else if (g.PenaltyWinnerTeamId == g.AwayTeamId) { a.WPen++; h.L++; }
            rows[g.HomeTeamId] = h;
            rows[g.AwayTeamId] = a;
        }

        var scored = rows.Select(r => (Team: r.Key, r.Value, Wins: r.Value.WReg + r.Value.WPen)).ToList();
        var best = scored
            .OrderByDescending(s => s.Wins)
            .ThenByDescending(s => s.Value.WReg)
            .ThenByDescending(s => s.Value.GF - s.Value.GA)
            .ThenByDescending(s => s.Value.GF)
            .First();
        bool Ties(int wins, int wReg, int gd, int gf)
            => wins == best.Wins && wReg == best.Value.WReg && gd == best.Value.GF - best.Value.GA && gf == best.Value.GF;

        return scored.Select(s => new TeamStanding(
            s.Team, s.Value.P, s.Wins, 0, s.Value.L, s.Value.GF, s.Value.GA, s.Wins,
            s.Value.P > 0 && Ties(s.Wins, s.Value.WReg, s.Value.GF - s.Value.GA, s.Value.GF),
            WinsRegular: s.Value.WReg,
            WinsPenalties: s.Value.WPen)).ToList();
    }

    /// <summary>Linhas individuais da rodada: gols (sem gol contra), vitórias do time, defesas de pênalti e prêmios.</summary>
    public static IReadOnlyList<PlayerRoundLine> PlayerLines(RoundSheet sheet, IReadOnlyCollection<Guid> teamIds)
    {
        var standings = Standings(sheet, teamIds).ToDictionary(s => s.TeamId);
        var finishedOrders = sheet.Games.Where(g => g.Status == RoundGameStatus.Finished).Select(g => g.Order).ToHashSet();
        var savesByKeeper = sheet.Games
            .Where(g => g.Status == RoundGameStatus.Finished && g.PenaltySaves is { Count: > 0 })
            .SelectMany(g => g.PenaltySaves!)
            .GroupBy(s => s.GoalkeeperProfileId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        return sheet.Participants.Select(p => new PlayerRoundLine(
            p.PlayerProfileId,
            p.TeamId,
            p.Present,
            p.PlayedGoalkeeper,
            p.Present ? sheet.Goals.Count(g => g.PlayerProfileId == p.PlayerProfileId && !g.OwnGoal && (sheet.RulesVersion < 2 || finishedOrders.Contains(g.GameOrder))) : 0,
            p.Present ? sheet.Goals.Count(g => g.PlayerProfileId == p.PlayerProfileId && g.OwnGoal && (sheet.RulesVersion < 2 || finishedOrders.Contains(g.GameOrder))) : 0,
            p.Present ? standings[p.TeamId].Wins : 0,
            p.Present && standings[p.TeamId].IsRoundWinner,
            sheet.BestPlayerId == p.PlayerProfileId,
            sheet.BestGoalkeeperId == p.PlayerProfileId,
            p.Present ? standings[p.TeamId].WinsPenalties : 0,
            p.Present ? savesByKeeper.GetValueOrDefault(p.PlayerProfileId, 0) : 0)).ToList();
    }

    /// <summary>Sugestão "Quem ganha fica": vencedor da última partida encerrada x 3ª cor que estava fora.</summary>
    public static (Guid HomeTeamId, Guid AwayTeamId)? SuggestNextMatchup(RoundSheet sheet, IReadOnlyList<Guid> orderedTeamIds)
    {
        if (orderedTeamIds.Count < 2) return null;
        if (sheet.Games.Any(g => g.Status == RoundGameStatus.InProgress)) return null;

        var lastFinished = sheet.Games
            .Where(g => g.Status == RoundGameStatus.Finished)
            .OrderByDescending(g => g.Order)
            .FirstOrDefault();

        if (lastFinished is null)
            return (orderedTeamIds[0], orderedTeamIds[1]);

        var winner = lastFinished.WinnerTeamId ?? lastFinished.HomeTeamId;
        var waiting = orderedTeamIds.FirstOrDefault(id => id != lastFinished.HomeTeamId && id != lastFinished.AwayTeamId);
        if (waiting != Guid.Empty)
            return (winner, waiting);

        var other = winner == lastFinished.HomeTeamId ? lastFinished.AwayTeamId : lastFinished.HomeTeamId;
        return (winner, other);
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
