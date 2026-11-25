using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

/// <summary>Jogador confirmado com as duas notas separadas (SO-1).</summary>
public sealed record DrawCandidate(
    Guid PlayerProfileId,
    string DisplayName,
    decimal LineRating,
    bool CanPlayGoalkeeper,
    decimal? GoalkeeperRating);

public sealed record ColorTeamMember(
    Guid PlayerProfileId,
    string DisplayName,
    TeamRole Role,
    decimal LineRating,
    bool CanPlayGoalkeeper,
    decimal? GoalkeeperRating);

public sealed record ColorTeam(string Color, int SortOrder, IReadOnlyList<ColorTeamMember> Members);

/// <summary>Métricas por time. Linha e goleiro nunca se somam.</summary>
public sealed record ColorTeamMetrics(
    string Color,
    int TotalCount,
    int LineCount,
    decimal LineSum,
    decimal LineAverage,
    ColorTeamMember? Goalkeeper);

/// <summary>Métricas do sorteio em dois eixos separados.</summary>
public sealed record DrawBalance(
    IReadOnlyList<ColorTeamMetrics> Teams,
    decimal LineAverageSpread,
    decimal LineSumSpread,
    int LineCountSpread,
    int TotalCountSpread,
    int GoalkeepersAssigned,
    decimal? GoalkeeperSpread,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers);

public sealed record ThreeColorDrawResult(
    string Seed,
    string AlgorithmVersion,
    IReadOnlyList<ColorTeam> Teams,
    DrawBalance Balance,
    int DistinctCandidates,
    int FinalistCandidates);

/// <summary>
/// Sorteio de Vermelho, Preto e Verde (SO-2/SO-3).
/// 1) Até um goleiro titular por time, escolhido entre os aptos; excedentes jogam na linha com a nota de linha.
/// 2) Tamanhos: total por time difere em até 1; goleiros vão para os times maiores, então a linha também difere em até 1.
/// 3) Gera candidatas distintas (busca local a partir de embaralhamentos com a semente).
/// 4) Filtra primeiro pela faixa de equilíbrio da LINHA (amplitude das médias), depois pela faixa dos GOLEIROS.
///    A nota de goleiro nunca entra na métrica de linha: um goleiro 10 não compensa uma linha fraca.
/// 5) Escolhe uma finalista e a atribuição de cores com a mesma semente: mesma entrada + semente + versão = mesma saída.
/// Tolerâncias e orçamento de busca são provisórios até o G2.
/// </summary>
public static class ThreeColorDraw
{
    public const string AlgorithmVersion = "3cores-v1";
    public static readonly IReadOnlyList<string> Colors = ["Vermelho", "Preto", "Verde"];
    public const decimal LineAverageTolerance = 0.25m;
    public const decimal GoalkeeperTolerance = 0.5m;
    private const int SearchBudget = 600;
    private const int MaxKeeperSets = 40;

    public static ThreeColorDrawResult Draw(IReadOnlyList<DrawCandidate> players, string? seed = null)
    {
        if (players.Count < Colors.Count)
            throw new DomainException("É preciso ao menos 3 confirmados para sortear Vermelho, Preto e Verde.");
        if (players.Select(p => p.PlayerProfileId).Distinct().Count() != players.Count)
            throw new DomainException("Jogador repetido na lista do sorteio.");

        seed = string.IsNullOrWhiteSpace(seed) ? Guid.NewGuid().ToString("N") : seed.Trim();
        var rng = new Random(StableHash($"{AlgorithmVersion}|{seed}"));

        // Ordem canônica: a saída não depende da ordem em que a lista chegou.
        var roster = players.OrderBy(p => p.PlayerProfileId).ToList();
        var n = roster.Count;
        var sizes = Enumerable.Range(0, 3).Select(i => n / 3 + (i < n % 3 ? 1 : 0)).ToArray();

        var apt = roster.Where(p => p.CanPlayGoalkeeper).ToList();
        // Linha primeiro: só reserva goleiro se sobrarem ao menos 3 jogadores de linha.
        var keeperSlots = Math.Min(Math.Min(apt.Count, 3), Math.Max(0, n - 3));
        var keeperSets = KeeperSets(apt, keeperSlots, rng);
        var perSet = Math.Max(30, SearchBudget / keeperSets.Count);

        var seen = new HashSet<string>();
        var candidates = new List<(int[][] Teams, int[] KeeperOf, decimal LineSpread, decimal? GkSpread)>();

        foreach (var keepers in keeperSets)
        {
            var keeperIds = keepers.Select(k => k.PlayerProfileId).ToHashSet();
            var line = roster.Where(p => !keeperIds.Contains(p.PlayerProfileId)).ToList();
            // Goleiros vão para os times maiores (mantém a linha com diferença de no máximo 1).
            var keeperTeams = Enumerable.Range(0, 3).OrderByDescending(i => sizes[i]).ThenBy(i => i).Take(keepers.Count).ToList();
            var lineSlots = Enumerable.Range(0, 3).Select(i => sizes[i] - (keeperTeams.Contains(i) ? 1 : 0)).ToArray();

            for (var attempt = 0; attempt < perSet; attempt++)
            {
                var shuffledKeepers = Shuffle(keepers, rng);
                var keeperOf = new int[3];
                Array.Fill(keeperOf, -1);
                for (var i = 0; i < keeperTeams.Count; i++)
                    keeperOf[keeperTeams[i]] = roster.IndexOf(shuffledKeepers[i]);

                var order = Shuffle(line, rng).Select(p => roster.IndexOf(p)).ToList();
                var teams = new int[3][];
                var pos = 0;
                for (var t = 0; t < 3; t++)
                {
                    teams[t] = order.Skip(pos).Take(lineSlots[t]).ToArray();
                    pos += lineSlots[t];
                }

                ImproveLine(teams, roster);

                var key = CanonicalKey(teams, keeperOf, roster);
                if (!seen.Add(key)) continue;
                candidates.Add((teams, keeperOf, LineAverageSpread(teams, roster), GoalkeeperSpread(keeperOf, roster)));
            }
        }

        // Etapa 1: faixa de equilíbrio da linha.
        var bestLine = candidates.Min(c => c.LineSpread);
        var lineFinalists = candidates.Where(c => c.LineSpread <= bestLine + LineAverageTolerance).ToList();

        // Etapa 2: dentro da faixa de linha, faixa dos goleiros (sem misturar os eixos).
        var finalists = lineFinalists;
        var withGk = lineFinalists.Where(c => c.GkSpread.HasValue).ToList();
        if (withGk.Count > 0)
        {
            var bestGk = withGk.Min(c => c.GkSpread!.Value);
            finalists = withGk.Where(c => c.GkSpread!.Value <= bestGk + GoalkeeperTolerance).ToList();
        }

        var chosen = finalists[rng.Next(finalists.Count)];
        var colorOrder = Shuffle(Enumerable.Range(0, 3).ToList(), rng); // índice do time sorteado para cada cor

        var result = new List<ColorTeam>();
        for (var c = 0; c < 3; c++)
        {
            var t = colorOrder[c];
            var members = new List<ColorTeamMember>();
            if (chosen.KeeperOf[t] >= 0)
                members.Add(ToMember(roster[chosen.KeeperOf[t]], TeamRole.Goalkeeper));
            members.AddRange(chosen.Teams[t]
                .Select(i => roster[i])
                .OrderByDescending(p => p.LineRating).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(p => ToMember(p, TeamRole.Line)));
            result.Add(new ColorTeam(Colors[c], c, members));
        }

        var balance = Evaluate(result);
        return new ThreeColorDrawResult(seed, AlgorithmVersion, result, balance, candidates.Count, finalists.Count);
    }

    /// <summary>Métricas, avisos e bloqueios de qualquer escalação (prévia, após troca ou sorteio salvo).</summary>
    public static DrawBalance Evaluate(IReadOnlyList<ColorTeam> teams)
    {
        var metrics = teams.OrderBy(t => t.SortOrder).Select(t =>
        {
            var line = t.Members.Where(m => m.Role == TeamRole.Line).ToList();
            var sum = line.Sum(m => m.LineRating);
            var avg = line.Count == 0 ? 0m : Math.Round(sum / line.Count, 2);
            var keeper = t.Members.FirstOrDefault(m => m.Role == TeamRole.Goalkeeper);
            return new ColorTeamMetrics(t.Color, t.Members.Count, line.Count, sum, avg, keeper);
        }).ToList();

        var warnings = new List<string>();
        var blockers = new List<string>();

        foreach (var m in metrics)
        {
            if (m.LineCount == 0) blockers.Add($"{m.Color} está sem jogador de linha.");
            if (m.Goalkeeper is null) warnings.Add($"{m.Color} sem goleiro apto: definir rodízio ou substituto.");
            else if (m.Goalkeeper.GoalkeeperRating is null) warnings.Add($"Goleiro do {m.Color} ({m.Goalkeeper.DisplayName}) está sem nota de goleiro.");
        }
        if (metrics.Sum(m => m.LineCount) < 3) blockers.Add("Menos de três jogadores de linha para os três times.");
        if (teams.Any(t => t.Members.Count(m => m.Role == TeamRole.Goalkeeper) > 1))
            blockers.Add("Um time ficou com mais de um goleiro titular.");
        var aptOnLine = teams.SelectMany(t => t.Members).Where(m => m.Role == TeamRole.Line && m.CanPlayGoalkeeper).Select(m => m.DisplayName).ToList();
        if (aptOnLine.Count > 0)
            warnings.Add($"Aptos ao gol jogando na linha: {string.Join(", ", aptOnLine)}.");

        var totalSpread = metrics.Count == 0 ? 0 : metrics.Max(m => m.TotalCount) - metrics.Min(m => m.TotalCount);
        var lineCountSpread = metrics.Count == 0 ? 0 : metrics.Max(m => m.LineCount) - metrics.Min(m => m.LineCount);
        if (totalSpread > 1 || lineCountSpread > 1)
            warnings.Add("Diferença de mais de um jogador entre os times.");

        var rated = metrics.Where(m => m.Goalkeeper?.GoalkeeperRating is not null).Select(m => m.Goalkeeper!.GoalkeeperRating!.Value).ToList();
        return new DrawBalance(
            metrics,
            metrics.Count == 0 ? 0 : metrics.Max(m => m.LineAverage) - metrics.Min(m => m.LineAverage),
            metrics.Count == 0 ? 0 : metrics.Max(m => m.LineSum) - metrics.Min(m => m.LineSum),
            lineCountSpread,
            totalSpread,
            metrics.Count(m => m.Goalkeeper is not null),
            rated.Count >= 2 ? rated.Max() - rated.Min() : null,
            warnings,
            blockers);
    }

    private static ColorTeamMember ToMember(DrawCandidate p, TeamRole role)
        => new(p.PlayerProfileId, p.DisplayName, role, p.LineRating, p.CanPlayGoalkeeper, p.GoalkeeperRating);

    private static List<List<DrawCandidate>> KeeperSets(List<DrawCandidate> apt, int slots, Random rng)
    {
        if (slots == 0) return [[]];
        if (apt.Count <= slots) return [apt.ToList()];

        var all = Combinations(apt, slots).ToList();
        if (all.Count <= MaxKeeperSets) return all;

        // Muitos aptos: amostra com a semente, sempre incluindo o trio de maiores notas de goleiro.
        var best = apt.OrderByDescending(p => p.GoalkeeperRating ?? -1m).ThenBy(p => p.PlayerProfileId).Take(slots).ToList();
        var sample = Shuffle(all, rng).Take(MaxKeeperSets - 1).ToList();
        sample.Add(best);
        return sample;
    }

    private static IEnumerable<List<DrawCandidate>> Combinations(List<DrawCandidate> items, int k, int start = 0)
    {
        if (k == 0) { yield return []; yield break; }
        for (var i = start; i <= items.Count - k; i++)
            foreach (var rest in Combinations(items, k - 1, i + 1))
            {
                rest.Insert(0, items[i]);
                yield return rest;
            }
    }

    /// <summary>Busca local: troca jogadores de linha entre times enquanto a amplitude das médias (e depois das somas) diminuir.</summary>
    private static void ImproveLine(int[][] teams, List<DrawCandidate> roster)
    {
        var current = (LineAverageSpread(teams, roster), LineSumSpread(teams, roster));
        for (var pass = 0; pass < 30; pass++)
        {
            var improved = false;
            for (var a = 0; a < 3; a++)
            for (var b = a + 1; b < 3; b++)
            for (var i = 0; i < teams[a].Length; i++)
            for (var j = 0; j < teams[b].Length; j++)
            {
                (teams[a][i], teams[b][j]) = (teams[b][j], teams[a][i]);
                var next = (LineAverageSpread(teams, roster), LineSumSpread(teams, roster));
                if (next.Item1 < current.Item1 || (next.Item1 == current.Item1 && next.Item2 < current.Item2))
                {
                    current = next;
                    improved = true;
                }
                else
                {
                    (teams[a][i], teams[b][j]) = (teams[b][j], teams[a][i]);
                }
            }
            if (!improved) break;
        }
    }

    private static decimal LineAverageSpread(int[][] teams, List<DrawCandidate> roster)
    {
        var avgs = teams.Select(t => t.Length == 0 ? 0m : t.Sum(i => roster[i].LineRating) / t.Length).ToList();
        return Math.Round(avgs.Max() - avgs.Min(), 4);
    }

    private static decimal LineSumSpread(int[][] teams, List<DrawCandidate> roster)
    {
        var sums = teams.Select(t => t.Sum(i => roster[i].LineRating)).ToList();
        return sums.Max() - sums.Min();
    }

    private static decimal? GoalkeeperSpread(int[] keeperOf, List<DrawCandidate> roster)
    {
        var rated = keeperOf.Where(i => i >= 0 && roster[i].GoalkeeperRating.HasValue)
            .Select(i => roster[i].GoalkeeperRating!.Value).ToList();
        return rated.Count >= 2 ? rated.Max() - rated.Min() : null;
    }

    private static string CanonicalKey(int[][] teams, int[] keeperOf, List<DrawCandidate> roster)
        => string.Join("|", Enumerable.Range(0, 3)
            .Select(t => $"{(keeperOf[t] >= 0 ? roster[keeperOf[t]].PlayerProfileId.ToString("N") : "-")}:" +
                         string.Join(",", teams[t].Select(i => roster[i].PlayerProfileId.ToString("N")).OrderBy(x => x, StringComparer.Ordinal)))
            .OrderBy(x => x, StringComparer.Ordinal));

    private static List<T> Shuffle<T>(IEnumerable<T> source, Random rng)
    {
        var list = source.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in s)
                hash = hash * 31 + c;
            return hash;
        }
    }
}
