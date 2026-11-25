using System.Globalization;
using System.Text.Json;
using FutebolAtemporal.Application.Results;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

/// <summary>
/// Pós-jogo de três cores (PO-1/PO-2/PO-3): fechamento, correção auditada com motivo, consulta de revisões
/// e estatísticas da temporada (artilharia mensal/anual, cores, rodadas, prêmios).
/// Só a revisão vigente entra nas contas, então corrigir não gera dupla contagem.
/// </summary>
public sealed class RoundResultService : IRoundResultService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;

    public RoundResultService(AppDbContext db) => _db = db;

    public async Task<RoundResultDto> CloseRoundAsync(Guid userId, Guid matchId, RoundSheetRequest request, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);

        if (!MatchTransitions.CanClose(match.Status))
            throw new DomainException("Partida ainda não pode ser encerrada.");
        if (await _db.MatchResults.AnyAsync(r => r.MatchId == matchId, ct)
            || await _db.MatchResultRevisions.AnyAsync(r => r.MatchId == matchId, ct))
            throw new DomainException("Resultado já registrado. Para mudar, use a correção com motivo.");

        var draw = await LoadColorDrawAsync(matchId, ct);
        var sheet = BuildSheet(request, draw);
        RoundRules.Validate(sheet, TeamIds(draw));

        _db.MatchResultRevisions.Add(new MatchResultRevision
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Revision = 1,
            IsCurrent = true,
            SheetJson = JsonSerializer.Serialize(sheet, Json),
            CreatedByUserId = userId
        });
        await ApplyDerivedAsync(match, draw, sheet, userId, ct);

        match.Status = MatchStatus.Completed;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++;
        Audit(match, userId, "result.closed", new { revision = 1, games = sheet.Games.Count, goals = sheet.Goals.Count });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetRoundAsync(userId, matchId, null, ct))!;
    }

    public async Task<RoundResultDto> CorrectRoundAsync(Guid userId, Guid matchId, RoundSheetRequest request, CancellationToken ct = default)
    {
        RoundRules.EnsureReason(request.Reason);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        if (match.Status != MatchStatus.Completed)
            throw new DomainException("Só é possível corrigir uma rodada encerrada.");

        var current = await _db.MatchResultRevisions.FirstOrDefaultAsync(r => r.MatchId == matchId && r.IsCurrent, ct)
            ?? throw new DomainException("Rodada antiga (Time A/B) ou sem súmula de três cores: correção indisponível.");

        var draw = await LoadColorDrawAsync(matchId, ct);
        var sheet = BuildSheet(request, draw);
        RoundRules.Validate(sheet, TeamIds(draw));

        var next = await _db.MatchResultRevisions.Where(r => r.MatchId == matchId).MaxAsync(r => r.Revision, ct) + 1;
        current.IsCurrent = false; // a revisão anterior continua consultável
        _db.MatchResultRevisions.Add(new MatchResultRevision
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Revision = next,
            IsCurrent = true,
            SheetJson = JsonSerializer.Serialize(sheet, Json),
            Reason = request.Reason!.Trim(),
            CreatedByUserId = userId
        });
        await ApplyDerivedAsync(match, draw, sheet, userId, ct);

        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++; // duas correções simultâneas: a segunda falha por concorrência (409)
        Audit(match, userId, "result.corrected", new { revision = next, previous = current.Revision, reason = request.Reason!.Trim() });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetRoundAsync(userId, matchId, null, ct))!;
    }

    public async Task<RoundResultDto?> GetRoundAsync(Guid userId, Guid matchId, int? revision, CancellationToken ct = default)
    {
        var match = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return null;
        var (membership, _) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        var isAdmin = membership.Role == GroupRole.Admin;
        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var revisions = await _db.MatchResultRevisions.AsNoTracking().Where(r => r.MatchId == matchId).ToListAsync(ct);
        var result = await _db.MatchResults.AsNoTracking().FirstOrDefaultAsync(r => r.MatchId == matchId, ct);

        if (revisions.Count == 0)
        {
            if (result is null) return null;
            // Legado A/B: cores, goleiros e gols não existiam -> indisponíveis.
            var award = await _db.PlayerAwards.AsNoTracking()
                .FirstOrDefaultAsync(a => a.MatchId == matchId && a.Type == AwardType.ManOfTheMatch, ct);
            var legacyNames = award is null ? new Dictionary<Guid, string>()
                : await AccessHelper.DisplayNamesForProfilesAsync(_db, [award.PlayerProfileId], ct);
            return new RoundResultDto(matchId, true, null, true, result.ClosedAt, localDate, [], [], [],
                award?.PlayerProfileId, award is null ? null : legacyNames.GetValueOrDefault(award.PlayerProfileId, "Jogador"),
                null, null, result.Notes, result.HomeScore, result.AwayScore, result.WinnerTeamId, []);
        }

        var chosen = revision is { } n
            ? revisions.FirstOrDefault(r => r.Revision == n)
            : revisions.FirstOrDefault(r => r.IsCurrent);
        if (chosen is null) return null;
        if (!chosen.IsCurrent && !isAdmin)
            throw new ForbiddenException("Revisões anteriores são visíveis só para administradores.");

        var sheet = JsonSerializer.Deserialize<RoundSheet>(chosen.SheetJson, Json)!;
        var draw = await LoadColorDrawAsync(matchId, ct);
        var teamIds = TeamIds(draw);
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, sheet.Participants.Select(p => p.PlayerProfileId), ct);
        string Name(Guid id) => names.GetValueOrDefault(id, "Jogador");
        var teamById = draw.Teams.ToDictionary(t => t.Id);
        var byPlayer = sheet.Participants.ToDictionary(p => p.PlayerProfileId);

        var standings = RoundRules.Standings(sheet, teamIds);
        var lines = RoundRules.PlayerLines(sheet, teamIds).ToDictionary(l => l.PlayerProfileId);

        var teams = draw.Teams.OrderBy(t => t.SortOrder).Select(t =>
        {
            var s = standings.First(x => x.TeamId == t.Id);
            return new RoundTeamDto(t.Id, t.Name, ColorOf(t), s.Played, s.Wins, s.Draws, s.Losses, s.GoalsFor, s.GoalsAgainst, s.Points, s.IsRoundWinner);
        }).ToList();
        var games = sheet.Games.OrderBy(g => g.Order).Select(g => new RoundGameDto(
            g.Order, g.HomeTeamId, g.AwayTeamId, g.HomeScore, g.AwayScore,
            g.HomeScore > g.AwayScore ? "HomeWin" : g.HomeScore < g.AwayScore ? "AwayWin" : "Draw",
            sheet.Goals.Where(x => x.GameOrder == g.Order)
                .Select(x => new RoundGoalDto(x.PlayerProfileId, Name(x.PlayerProfileId), byPlayer[x.PlayerProfileId].TeamId, x.OwnGoal))
                .ToList())).ToList();
        var participants = sheet.Participants
            .OrderBy(p => teamById[p.TeamId].SortOrder).ThenByDescending(p => p.PlayedGoalkeeper).ThenBy(p => Name(p.PlayerProfileId), StringComparer.OrdinalIgnoreCase)
            .Select(p => new RoundParticipantDto(p.PlayerProfileId, Name(p.PlayerProfileId), p.TeamId, p.Present, p.PlayedGoalkeeper,
                lines[p.PlayerProfileId].Goals, lines[p.PlayerProfileId].OwnGoals)).ToList();

        var history = isAdmin
            ? revisions.OrderBy(r => r.Revision).Select(r => new RoundRevisionInfoDto(r.Revision, r.IsCurrent, r.CreatedAt, r.Reason)).ToList()
            : [];

        return new RoundResultDto(
            matchId, false, chosen.Revision, chosen.IsCurrent, result?.ClosedAt ?? chosen.CreatedAt, localDate,
            teams, games, participants,
            sheet.BestPlayerId, sheet.BestPlayerId is { } bp ? Name(bp) : null,
            sheet.BestGoalkeeperId, sheet.BestGoalkeeperId is { } bg ? Name(bg) : null,
            sheet.Notes, null, null, null, history);
    }

    public async Task<SeasonStatsDto> GetSeasonAsync(Guid userId, Guid groupId, int? year, int? month, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);
        if (month is < 1 or > 12) throw new DomainException("Mês inválido.");
        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
        var today = GroupClock.LocalDate(DateTimeOffset.UtcNow, group.TimeZoneId);
        var y = year ?? today.Year;
        // Mês dos artilheiros: o pedido; sem pedido, o mês corrente quando o ano é o atual.
        int? scorerMonth = month ?? (y == today.Year && year is null ? today.Month : null);

        var matches = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Completed)
            .ToListAsync(ct);
        var inYear = matches
            .Select(m => (Match: m, Local: GroupClock.LocalDate(m.StartsAt, group.TimeZoneId)))
            .Where(x => x.Local.Year == y)
            .ToList();
        var ids = inYear.Select(x => x.Match.Id).ToList();

        var revisions = await _db.MatchResultRevisions.AsNoTracking()
            .Where(r => ids.Contains(r.MatchId) && r.IsCurrent).ToListAsync(ct);
        var draws = await _db.TeamDraws.AsNoTracking().Include(d => d.Teams).ThenInclude(t => t.Members)
            .Where(d => ids.Contains(d.MatchId) && d.Status == TeamDrawStatus.Confirmed).ToListAsync(ct);
        var legacyResults = await _db.MatchResults.AsNoTracking().Where(r => ids.Contains(r.MatchId)).ToListAsync(ct);
        var legacyAwards = await _db.PlayerAwards.AsNoTracking()
            .Where(a => ids.Contains(a.MatchId) && a.Type == AwardType.ManOfTheMatch).ToListAsync(ct);
        var legacyRegs = await _db.MatchRegistrations.AsNoTracking()
            .Where(r => ids.Contains(r.MatchId) && r.Status == RegistrationStatus.Participated).ToListAsync(ct);

        var rounds = new List<(Match Match, DateTime Local, RoundSheet? Sheet, TeamDraw? Draw)>();
        foreach (var (m, local) in inYear)
        {
            var rev = revisions.FirstOrDefault(r => r.MatchId == m.Id);
            var draw = draws.FirstOrDefault(d => d.MatchId == m.Id);
            rounds.Add((m, local, rev is null ? null : JsonSerializer.Deserialize<RoundSheet>(rev.SheetJson, Json), draw));
        }

        bool InPeriod(DateTime local, int? mth) => mth is null || local.Month == mth;
        var period = rounds.Where(r => InPeriod(r.Local, month)).OrderByDescending(r => r.Local).ToList();

        // Artilharia: só súmulas de três cores (rodadas antigas não têm gols), gol contra fora.
        IEnumerable<(Guid, int)> GoalsOf(IEnumerable<(Match Match, DateTime Local, RoundSheet? Sheet, TeamDraw? Draw)> rs)
            => rs.Where(r => r.Sheet is not null && r.Draw is not null)
                .SelectMany(r => RoundRules.PlayerLines(r.Sheet!, TeamIds(r.Draw!)))
                .Select(l => (l.PlayerProfileId, l.Goals));
        var yearScorers = ScorerTable.Rank(GoalsOf(rounds));
        var monthScorers = scorerMonth is null ? [] : ScorerTable.Rank(GoalsOf(rounds.Where(r => r.Local.Month == scorerMonth)));

        // Cores e jogadores no período.
        var colors = ThreeColorDraw.Colors.ToDictionary(c => c, _ => new int[6]); // roundsWon, W, D, L, GF, GA
        var players = new Dictionary<Guid, int[]>(); // played, roundsWon, gamesWon, goals, ownGoals, best, bestGk
        int[] Row(Guid id) => players.TryGetValue(id, out var r) ? r : players[id] = new int[7];

        foreach (var r in period)
        {
            if (r.Sheet is not null && r.Draw is not null)
            {
                var teamIds = TeamIds(r.Draw);
                var colorOf = r.Draw.Teams.ToDictionary(t => t.Id, ColorOf);
                foreach (var s in RoundRules.Standings(r.Sheet, teamIds))
                {
                    if (colorOf[s.TeamId] is not { } c || !colors.TryGetValue(c, out var acc)) continue;
                    if (s.IsRoundWinner) acc[0]++;
                    acc[1] += s.Wins; acc[2] += s.Draws; acc[3] += s.Losses; acc[4] += s.GoalsFor; acc[5] += s.GoalsAgainst;
                }
                foreach (var l in RoundRules.PlayerLines(r.Sheet, teamIds))
                {
                    var row = Row(l.PlayerProfileId);
                    if (l.Present) row[0]++;
                    if (l.RoundWon) row[1]++;
                    row[2] += l.GamesWon; row[3] += l.Goals; row[4] += l.OwnGoals;
                    if (l.BestPlayer) row[5]++;
                    if (l.BestGoalkeeper) row[6]++;
                }
            }
            else
            {
                // Legado A/B: participação, vitória pelo time vencedor e craque; sem gols, cores ou goleiro.
                var result = legacyResults.FirstOrDefault(x => x.MatchId == r.Match.Id);
                foreach (var reg in legacyRegs.Where(x => x.MatchId == r.Match.Id))
                {
                    var row = Row(reg.PlayerProfileId);
                    row[0]++;
                    var team = r.Draw?.Teams.FirstOrDefault(t => t.Members.Any(mm => mm.PlayerProfileId == reg.PlayerProfileId));
                    if (team is not null && result?.WinnerTeamId == team.Id) row[1]++;
                }
                foreach (var a in legacyAwards.Where(x => x.MatchId == r.Match.Id))
                    Row(a.PlayerProfileId)[5]++;
            }
        }

        var allIds = players.Keys.Concat(yearScorers.Select(s => s.PlayerProfileId)).Distinct().ToList();
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, allIds, ct);
        string Name(Guid id) => names.GetValueOrDefault(id, "Jogador");
        ScorerDto ToDto(ScorerRow s) => new(s.PlayerProfileId, Name(s.PlayerProfileId), s.Goals, s.Position, s.IsLeader);

        var roundDtos = period.Select(r =>
        {
            var local = r.Local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (r.Sheet is null || r.Draw is null)
                return new SeasonRoundDto(r.Match.Id, r.Match.Title, local, true, [], []);
            var colorOf = r.Draw.Teams.ToDictionary(t => t.Id, t => ColorOf(t) ?? t.Name);
            var winners = RoundRules.Standings(r.Sheet, TeamIds(r.Draw)).Where(s => s.IsRoundWinner).Select(s => colorOf[s.TeamId]).ToList();
            var games = r.Sheet.Games.OrderBy(g => g.Order)
                .Select(g => $"{colorOf[g.HomeTeamId]} {g.HomeScore} x {g.AwayScore} {colorOf[g.AwayTeamId]}").ToList();
            return new SeasonRoundDto(r.Match.Id, r.Match.Title, local, false, winners, games);
        }).ToList();

        return new SeasonStatsDto(
            y, month, group.TimeZoneId,
            monthScorers.Select(ToDto).ToList(),
            yearScorers.Select(ToDto).ToList(),
            colors.Select(c => new ColorStandingDto(c.Key, c.Value[0], c.Value[1], c.Value[2], c.Value[3], c.Value[4], c.Value[5])).ToList(),
            players.Select(p => new SeasonPlayerDto(p.Key, Name(p.Key), p.Value[0], p.Value[1], p.Value[2], p.Value[3], p.Value[4], p.Value[5], p.Value[6]))
                .OrderByDescending(p => p.Goals).ThenByDescending(p => p.RoundsWon).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            roundDtos);
    }

    // ---------- auxiliares ----------

    private async Task<TeamDraw> LoadColorDrawAsync(Guid matchId, CancellationToken ct)
    {
        var draw = (await _db.TeamDraws.Include(d => d.Teams).ThenInclude(t => t.Members)
                .Where(d => d.MatchId == matchId && d.Status == TeamDrawStatus.Confirmed)
                .ToListAsync(ct))
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault()
            ?? throw new DomainException("Confirme o sorteio dos times antes de registrar o resultado.");
        if (draw.AlgorithmVersion is null)
            throw new DomainException("Sorteio antigo (Time A/B): use o fechamento antigo; súmula de três cores indisponível.");
        return draw;
    }

    private static IReadOnlyCollection<Guid> TeamIds(TeamDraw draw) => draw.Teams.Select(t => t.Id).ToList();

    private static string? ColorOf(MatchTeam team) => ThreeColorDraw.Colors.Contains(team.Name) ? team.Name : null;

    /// <summary>Monta a súmula a partir do sorteio confirmado: quem não veio na presença conta como presente.</summary>
    private static RoundSheet BuildSheet(RoundSheetRequest request, TeamDraw draw)
    {
        var members = draw.Teams.SelectMany(t => t.Members.Select(m => (Member: m, TeamId: t.Id))).ToList();
        var memberIds = members.Select(m => m.Member.PlayerProfileId).ToHashSet();
        var presence = request.Presence ?? [];
        if (presence.Any(p => !memberIds.Contains(p.PlayerProfileId)))
            throw new DomainException("Presença informada para quem não está no sorteio confirmado.");
        if (presence.Select(p => p.PlayerProfileId).Distinct().Count() != presence.Count)
            throw new DomainException("Jogador repetido na presença.");

        var participants = members.Select(m =>
        {
            var input = presence.FirstOrDefault(p => p.PlayerProfileId == m.Member.PlayerProfileId);
            return new RoundParticipant(
                m.Member.PlayerProfileId, m.TeamId,
                input?.Present ?? true,
                input?.PlayedGoalkeeper ?? m.Member.Role == TeamRole.Goalkeeper);
        }).ToList();

        var games = (request.Games ?? []).Select((g, i) => new RoundGame(i + 1, g.HomeTeamId, g.AwayTeamId, g.HomeScore, g.AwayScore)).ToList();
        var goals = (request.Goals ?? []).Select(g => new RoundGoal(g.Game, g.PlayerProfileId, g.OwnGoal)).ToList();
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        return new RoundSheet(participants, games, goals, request.BestPlayerProfileId, request.BestGoalkeeperProfileId, notes);
    }

    /// <summary>
    /// Mantém as tabelas legadas coerentes com a revisão vigente: presença nas inscrições, prêmios e
    /// vencedor único (para o ranking antigo). Atualiza no lugar para nunca duplicar.
    /// </summary>
    private async Task ApplyDerivedAsync(Match match, TeamDraw draw, RoundSheet sheet, Guid userId, CancellationToken ct)
    {
        var presence = sheet.Participants.ToDictionary(p => p.PlayerProfileId, p => p.Present);
        var regs = await _db.MatchRegistrations
            .Where(r => r.MatchId == match.Id && presence.Keys.Contains(r.PlayerProfileId)).ToListAsync(ct);
        foreach (var reg in regs)
        {
            reg.Status = presence[reg.PlayerProfileId] ? RegistrationStatus.Participated : RegistrationStatus.Absent;
            reg.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var awards = await _db.PlayerAwards.Where(a => a.MatchId == match.Id).ToListAsync(ct);
        UpsertAward(awards, match.Id, AwardType.ManOfTheMatch, sheet.BestPlayerId, userId);
        UpsertAward(awards, match.Id, AwardType.BestGoalkeeper, sheet.BestGoalkeeperId, userId);

        var winners = RoundRules.Standings(sheet, TeamIds(draw)).Where(s => s.IsRoundWinner).Select(s => s.TeamId).ToList();
        var result = await _db.MatchResults.FirstOrDefaultAsync(r => r.MatchId == match.Id, ct);
        if (result is null)
        {
            result = new MatchResult { Id = Guid.NewGuid(), MatchId = match.Id, ClosedByUserId = userId };
            _db.MatchResults.Add(result);
        }
        result.HomeScore = 0; // placar A/B não se aplica a três cores; os confrontos ficam na súmula
        result.AwayScore = 0;
        result.WinnerTeamId = winners.Count == 1 ? winners[0] : null;
        result.Notes = sheet.Notes;
    }

    private void UpsertAward(List<PlayerAward> existing, Guid matchId, AwardType type, Guid? playerId, Guid userId)
    {
        var current = existing.FirstOrDefault(a => a.Type == type);
        if (playerId is null)
        {
            if (current is not null) _db.PlayerAwards.Remove(current);
            return;
        }
        if (current is null)
            _db.PlayerAwards.Add(new PlayerAward { Id = Guid.NewGuid(), MatchId = matchId, Type = type, PlayerProfileId = playerId.Value, AwardedByUserId = userId });
        else
        {
            current.PlayerProfileId = playerId.Value;
            current.AwardedByUserId = userId;
        }
    }

    private void Audit(Match match, Guid userId, string action, object payload)
        => _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = match.GroupId,
            ActorUserId = userId,
            Action = action,
            EntityType = nameof(MatchResultRevision),
            EntityId = match.Id,
            PayloadJson = JsonSerializer.Serialize(payload)
        });
}

/// <summary>Data local do jogo no fuso do grupo (base de mês/ano da artilharia).</summary>
public static class GroupClock
{
    public static DateTime LocalDate(DateTimeOffset instant, string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).Date;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return instant.UtcDateTime.Date;
        }
    }
}
