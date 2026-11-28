using FutebolAtemporal.Application.Notifications;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FutebolAtemporal.Application.Results;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

/// <summary>
/// Pós-jogo e modo ao vivo de três cores (PO-1/PO-2/PO-3, D1–D6):
/// persistência jogo a jogo em MatchLiveSheets, fechamento com Melhores do Dia,
/// correção auditada com motivo, consulta de revisões e estatísticas da temporada.
/// Só a revisão vigente entra nas contas, então corrigir não gera dupla contagem.
/// </summary>
public sealed class RoundResultService : IRoundResultService
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();
    private readonly AppDbContext _db;
    private readonly INotificationTriggerService? _notifications;

    public RoundResultService(AppDbContext db, INotificationTriggerService? notifications = null)
    {
        _db = db;
        _notifications = notifications;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    // ---------- Modo Ao Vivo (D2–D6) ----------

    public async Task<LiveDayDto?> GetLiveAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        var match = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return null;
        await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);

        if (match.Status is not (MatchStatus.Drawn or MatchStatus.InProgress or MatchStatus.AwaitingClose or MatchStatus.Completed))
            throw new ConflictException("A pelada ainda não tem sorteio confirmado para modo ao vivo.", "day_not_started");

        var draw = await LoadColorDrawForLiveAsync(matchId, ct);
        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        RoundSheet sheet;
        int version;
        if (match.Status == MatchStatus.Completed)
        {
            var rev = await _db.MatchResultRevisions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.MatchId == matchId && r.IsCurrent, ct);
            if (rev is null) return null;
            sheet = DeserializeSheet(rev.SheetJson);
            var liveRow = await _db.MatchLiveSheets.AsNoTracking().FirstOrDefaultAsync(l => l.MatchId == matchId, ct);
            version = liveRow?.Version ?? rev.Revision;
        }
        else
        {
            var liveRow = await _db.MatchLiveSheets.AsNoTracking().FirstOrDefaultAsync(l => l.MatchId == matchId, ct);
            sheet = liveRow is null ? CreateInitialLiveSheet(draw) : DeserializeSheet(liveRow.SheetJson);
            version = liveRow?.Version ?? 0;
        }

        return await BuildLiveDayDtoAsync(match, localDate, version, draw, sheet, ct);
    }

    public async Task<LiveDayDto> StartOrInsertLiveGameAsync(Guid userId, Guid matchId, StartLiveGameRequest request, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (match, draw, live, sheet) = await LoadForLiveMutationAsync(userId, matchId, request.ExpectedVersion, allowDrawnStart: true, checkVersion: true, ct);

        if (match.Status == MatchStatus.Drawn)
        {
            match.Status = MatchStatus.InProgress;
            match.UpdatedAt = DateTimeOffset.UtcNow;
            match.Version++;
        }

        var teamIds = TeamIds(draw);
        var games = sheet.Games.OrderBy(g => g.Order).ToList();
        var goals = sheet.Goals.ToList();
        var participants = sheet.Participants.ToList();
        var byPlayer = participants.ToDictionary(p => p.PlayerProfileId);

        if (request.InsertAt is null)
        {
            if (games.Any(g => g.Status == RoundGameStatus.InProgress))
                throw new ConflictException("Já existe uma partida em andamento neste dia.", "game_in_progress");

            var newGame = new RoundGame(
                Order: games.Count + 1,
                HomeTeamId: request.HomeTeamId,
                AwayTeamId: request.AwayTeamId,
                HomeScore: 0,
                AwayScore: 0,
                Id: Guid.NewGuid(),
                Status: RoundGameStatus.InProgress,
                PenaltyWinnerTeamId: null,
                PenaltySaves: []);
            games.Add(newGame);
        }
        else
        {
            var pos = request.InsertAt.Value;
            if (pos < 1 || pos > games.Count + 1)
                throw new DomainException($"Posição de inserção inválida ({pos}).", "invalid_game");

            for (var i = 0; i < games.Count; i++)
            {
                if (games[i].Order >= pos)
                    games[i] = games[i] with { Order = games[i].Order + 1 };
            }
            for (var i = 0; i < goals.Count; i++)
            {
                if (goals[i].GameOrder >= pos)
                    goals[i] = goals[i] with { GameOrder = goals[i].GameOrder + 1 };
            }

            var insertedGoals = NormalizeLiveGoals(pos, request.Goals, participants);
            byPlayer = participants.ToDictionary(p => p.PlayerProfileId);
            var tempGame = new RoundGame(
                Order: pos,
                HomeTeamId: request.HomeTeamId,
                AwayTeamId: request.AwayTeamId,
                HomeScore: 0,
                AwayScore: 0,
                Id: Guid.NewGuid(),
                Status: RoundGameStatus.Finished,
                PenaltyWinnerTeamId: request.PenaltyWinnerTeamId,
                PenaltySaves: NormalizePenaltySaves(request.PenaltySaves));

            var (homeScore, awayScore) = ComputeScoresSafe(tempGame, insertedGoals, byPlayer);
            tempGame = tempGame with { HomeScore = homeScore, AwayScore = awayScore };

            games.Add(tempGame);
            games = games.OrderBy(g => g.Order).ToList();
            goals.AddRange(insertedGoals);
        }

        var nextSheet = sheet with
        {
            Participants = participants,
            Games = games,
            Goals = goals,
            RulesVersion = RoundRules.CurrentRulesVersion
        };
        RoundRules.ValidateLive(nextSheet, teamIds);

        var version = await SaveLiveSheetAsync(match, live, nextSheet, userId, ct);
        await tx.CommitAsync(ct);

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await BuildLiveDayDtoAsync(match, localDate, version, draw, nextSheet, ct);
    }

    public async Task<LiveDayDto> AddLiveGoalAsync(Guid userId, Guid matchId, Guid gameId, AddLiveGoalRequest request, CancellationToken ct = default)
    {
        if (request.GoalId == Guid.Empty)
            throw new DomainException("Identificador do gol (goalId) é obrigatório.", "invalid_game");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (match, draw, live, sheet) = await LoadForLiveMutationAsync(userId, matchId, request.ExpectedVersion, allowDrawnStart: false, checkVersion: false, ct);

        var gameIndex = sheet.Games.ToList().FindIndex(g => g.Id == gameId);
        if (gameIndex < 0)
            throw new NotFoundException("Partida do dia não encontrada.");

        var game = sheet.Games[gameIndex];

        // Idempotência por goalId: toque duplo ou retry com versão anterior retorna 200 sem duplicar.
        if (sheet.Goals.Any(g => g.GameOrder == game.Order && g.Id == request.GoalId))
        {
            var grp = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
            var ld = GroupClock.LocalDate(match.StartsAt, grp.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return await BuildLiveDayDtoAsync(match, ld, live?.Version ?? 0, draw, sheet, ct);
        }

        EnsureExpectedVersion(request.ExpectedVersion, live?.Version ?? 0);

        var participants = sheet.Participants.ToList();
        var pIdx = participants.FindIndex(p => p.PlayerProfileId == request.PlayerProfileId);
        if (pIdx < 0)
            throw new DomainException("Gol atribuído a quem não está escalado na rodada.", "invalid_game");

        if (!participants[pIdx].Present)
            participants[pIdx] = participants[pIdx] with { Present = true };

        var byPlayer = participants.ToDictionary(p => p.PlayerProfileId);
        var goals = sheet.Goals.ToList();
        goals.Add(new RoundGoal(game.Order, request.PlayerProfileId, request.OwnGoal, request.GoalId));

        var games = sheet.Games.ToList();
        var (homeScore, awayScore) = ComputeScoresSafe(game, goals, byPlayer);
        games[gameIndex] = game with { HomeScore = homeScore, AwayScore = awayScore };

        var nextSheet = sheet with
        {
            Participants = participants,
            Games = games,
            Goals = goals,
            RulesVersion = RoundRules.CurrentRulesVersion
        };
        RoundRules.ValidateLive(nextSheet, TeamIds(draw));

        var version = await SaveLiveSheetAsync(match, live, nextSheet, userId, ct);
        await tx.CommitAsync(ct);

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await BuildLiveDayDtoAsync(match, localDate, version, draw, nextSheet, ct);
    }

    public async Task<LiveDayDto> RemoveLiveGoalAsync(Guid userId, Guid matchId, Guid gameId, Guid goalId, int expectedVersion, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (match, draw, live, sheet) = await LoadForLiveMutationAsync(userId, matchId, expectedVersion, allowDrawnStart: false, checkVersion: true, ct);

        var games = sheet.Games.ToList();
        var gameIndex = games.FindIndex(g => g.Id == gameId);
        if (gameIndex < 0)
            throw new NotFoundException("Partida do dia não encontrada.");

        var game = games[gameIndex];
        var goals = sheet.Goals.ToList();
        var removed = goals.RemoveAll(g => g.GameOrder == game.Order && g.Id == goalId);
        if (removed == 0)
            throw new NotFoundException("Gol não encontrado nesta partida.");

        var byPlayer = sheet.Participants.ToDictionary(p => p.PlayerProfileId);
        var (homeScore, awayScore) = ComputeScoresSafe(game, goals, byPlayer);
        games[gameIndex] = game with { HomeScore = homeScore, AwayScore = awayScore };

        var nextSheet = sheet with
        {
            Games = games,
            Goals = goals,
            RulesVersion = RoundRules.CurrentRulesVersion
        };
        RoundRules.ValidateLive(nextSheet, TeamIds(draw));

        var version = await SaveLiveSheetAsync(match, live, nextSheet, userId, ct);
        await tx.CommitAsync(ct);

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await BuildLiveDayDtoAsync(match, localDate, version, draw, nextSheet, ct);
    }

    public async Task<LiveDayDto> FinishLiveGameAsync(Guid userId, Guid matchId, Guid gameId, FinishLiveGameRequest request, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (match, draw, live, sheet) = await LoadForLiveMutationAsync(userId, matchId, request.ExpectedVersion, allowDrawnStart: false, checkVersion: true, ct);

        var games = sheet.Games.ToList();
        var gameIndex = games.FindIndex(g => g.Id == gameId);
        if (gameIndex < 0)
            throw new NotFoundException("Partida do dia não encontrada.");

        var game = games[gameIndex];
        var byPlayer = sheet.Participants.ToDictionary(p => p.PlayerProfileId);
        var (homeScore, awayScore) = ComputeScoresSafe(game, sheet.Goals, byPlayer);

        games[gameIndex] = game with
        {
            HomeScore = homeScore,
            AwayScore = awayScore,
            Status = RoundGameStatus.Finished,
            PenaltyWinnerTeamId = request.PenaltyWinnerTeamId,
            PenaltySaves = NormalizePenaltySaves(request.PenaltySaves)
        };

        var nextSheet = sheet with
        {
            Games = games,
            RulesVersion = RoundRules.CurrentRulesVersion
        };
        RoundRules.ValidateLive(nextSheet, TeamIds(draw));

        var version = await SaveLiveSheetAsync(match, live, nextSheet, userId, ct);
        await tx.CommitAsync(ct);

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await BuildLiveDayDtoAsync(match, localDate, version, draw, nextSheet, ct);
    }

    public async Task<LiveDayDto> UpdateLiveGameAsync(Guid userId, Guid matchId, Guid gameId, UpdateLiveGameRequest request, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (match, draw, live, sheet) = await LoadForLiveMutationAsync(userId, matchId, request.ExpectedVersion, allowDrawnStart: false, checkVersion: true, ct);

        var games = sheet.Games.ToList();
        var gameIndex = games.FindIndex(g => g.Id == gameId);
        if (gameIndex < 0)
            throw new NotFoundException("Partida do dia não encontrada.");

        if (request.Status == RoundGameStatus.InProgress && games.Any(g => g.Id != gameId && g.Status == RoundGameStatus.InProgress))
            throw new ConflictException("Já existe outra partida em andamento neste dia.", "game_in_progress");

        var currentGame = games[gameIndex];
        var participants = sheet.Participants.ToList();
        var updatedGameGoals = NormalizeLiveGoals(currentGame.Order, request.Goals, participants);
        var byPlayer = participants.ToDictionary(p => p.PlayerProfileId);

        var goals = sheet.Goals.Where(g => g.GameOrder != currentGame.Order).ToList();
        goals.AddRange(updatedGameGoals);

        var updatedGame = currentGame with
        {
            HomeTeamId = request.HomeTeamId,
            AwayTeamId = request.AwayTeamId,
            Status = request.Status,
            PenaltyWinnerTeamId = request.PenaltyWinnerTeamId,
            PenaltySaves = NormalizePenaltySaves(request.PenaltySaves)
        };
        var (homeScore, awayScore) = ComputeScoresSafe(updatedGame, updatedGameGoals, byPlayer);
        games[gameIndex] = updatedGame with { HomeScore = homeScore, AwayScore = awayScore };

        var nextSheet = sheet with
        {
            Participants = participants,
            Games = games,
            Goals = goals,
            RulesVersion = RoundRules.CurrentRulesVersion
        };
        RoundRules.ValidateLive(nextSheet, TeamIds(draw));

        var version = await SaveLiveSheetAsync(match, live, nextSheet, userId, ct);
        await tx.CommitAsync(ct);

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await BuildLiveDayDtoAsync(match, localDate, version, draw, nextSheet, ct);
    }

    public async Task<LiveDayDto> DeleteLiveGameAsync(Guid userId, Guid matchId, Guid gameId, int expectedVersion, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (match, draw, live, sheet) = await LoadForLiveMutationAsync(userId, matchId, expectedVersion, allowDrawnStart: false, checkVersion: true, ct);

        var games = sheet.Games.OrderBy(g => g.Order).ToList();
        var target = games.FirstOrDefault(g => g.Id == gameId)
            ?? throw new NotFoundException("Partida do dia não encontrada.");

        var remainingGames = games.Where(g => g.Id != gameId).ToList();
        var orderMap = new Dictionary<int, int>();
        var renumberedGames = new List<RoundGame>(remainingGames.Count);
        for (var i = 0; i < remainingGames.Count; i++)
        {
            var newOrder = i + 1;
            orderMap[remainingGames[i].Order] = newOrder;
            renumberedGames.Add(remainingGames[i] with { Order = newOrder });
        }

        var renumberedGoals = sheet.Goals
            .Where(g => g.GameOrder != target.Order && orderMap.ContainsKey(g.GameOrder))
            .Select(g => g with { GameOrder = orderMap[g.GameOrder] })
            .ToList();

        var nextSheet = sheet with
        {
            Games = renumberedGames,
            Goals = renumberedGoals,
            RulesVersion = RoundRules.CurrentRulesVersion
        };
        RoundRules.ValidateLive(nextSheet, TeamIds(draw));

        var version = await SaveLiveSheetAsync(match, live, nextSheet, userId, ct);
        await tx.CommitAsync(ct);

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await BuildLiveDayDtoAsync(match, localDate, version, draw, nextSheet, ct);
    }

    public async Task<LiveDayDto> UpdateLivePresenceAsync(Guid userId, Guid matchId, UpdateLivePresenceRequest request, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var (match, draw, live, sheet) = await LoadForLiveMutationAsync(userId, matchId, request.ExpectedVersion, allowDrawnStart: true, checkVersion: true, ct);

        var participants = MergePresence(sheet.Participants, request.Presence, draw);
        var nextSheet = sheet with
        {
            Participants = participants,
            RulesVersion = RoundRules.CurrentRulesVersion
        };
        RoundRules.ValidateLive(nextSheet, TeamIds(draw));

        var version = await SaveLiveSheetAsync(match, live, nextSheet, userId, ct);
        await tx.CommitAsync(ct);

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var localDate = GroupClock.LocalDate(match.StartsAt, group.TimeZoneId).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await BuildLiveDayDtoAsync(match, localDate, version, draw, nextSheet, ct);
    }

    // ---------- Fechamento e Correção da Rodada ----------

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
        var live = await _db.MatchLiveSheets.FirstOrDefaultAsync(l => l.MatchId == matchId, ct);

        RoundSheet sheet;
        var useLiveSource = request.ExpectedLiveVersion.HasValue
            || (live is not null && (request.Games is null || request.Games.Count == 0));

        if (useLiveSource)
        {
            var currentVersion = live?.Version ?? 0;
            if (request.ExpectedLiveVersion.HasValue)
                EnsureExpectedVersion(request.ExpectedLiveVersion.Value, currentVersion);
            if (live is null)
                throw new DomainException("Registre ao menos um confronto da rodada antes de encerrar o dia.", "invalid_game");

            var liveSheet = DeserializeSheet(live.SheetJson);
            var mergedParticipants = request.Presence is { Count: > 0 }
                ? MergePresence(liveSheet.Participants, request.Presence, draw)
                : liveSheet.Participants;

            sheet = liveSheet with
            {
                Participants = mergedParticipants,
                BestPlayerId = request.BestPlayerProfileId,
                BestGoalkeeperId = request.BestGoalkeeperProfileId,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                RulesVersion = RoundRules.CurrentRulesVersion
            };
        }
        else
        {
            sheet = BuildSheet(request, draw);
        }

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

        var closedAt = DateTimeOffset.UtcNow;
        await MatchDayFeeLifecycle.LaunchOnCloseAsync(
            _db, match, sheet.Participants.Where(p => p.Present).Select(p => p.PlayerProfileId).ToList(), userId, closedAt, ct);

        match.Status = MatchStatus.Completed;
        match.UpdatedAt = closedAt;
        match.Version++;
        Audit(match, userId, "result.closed", new
        {
            revision = 1,
            rulesVersion = sheet.RulesVersion,
            games = sheet.Games.Count,
            goals = sheet.Goals.Count,
            penaltySaves = sheet.Games.SelectMany(g => g.PenaltySaves ?? []).Sum(s => s.Count)
        });

        if (_notifications is not null)
        {
            var participantIds = await _db.MatchRegistrations.AsNoTracking()
                .Where(r => r.MatchId == matchId && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Participated))
                .Select(r => r.PlayerProfileId)
                .ToListAsync(ct);
            var presentIds = sheet.Participants.Where(p => p.Present).Select(p => p.PlayerProfileId);
            await _notifications.EnqueueMatchCompletedAsync(match.GroupId, match.Id, participantIds.Concat(presentIds).Distinct().ToList(), ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _notifications?.SignalDispatcher();
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
        current.IsCurrent = false;
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
        await MatchDayFeeLifecycle.ApplyPresenceCorrectionAsync(
            _db, match, DeserializeSheet(current.SheetJson), sheet, userId, request.Reason!.Trim(), ct);

        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++;
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
            var award = await _db.PlayerAwards.AsNoTracking()
                .FirstOrDefaultAsync(a => a.MatchId == matchId && a.Type == AwardType.ManOfTheMatch, ct);
            var legacyNames = award is null ? new Dictionary<Guid, string>()
                : await AccessHelper.DisplayNamesForProfilesAsync(_db, [award.PlayerProfileId], ct);
            return new RoundResultDto(matchId, true, null, true, result.ClosedAt, localDate, [], [], [],
                award?.PlayerProfileId, award is null ? null : legacyNames.GetValueOrDefault(award.PlayerProfileId, "Jogador"),
                null, null, result.Notes, result.HomeScore, result.AwayScore, result.WinnerTeamId, [], RulesVersion: 1);
        }

        var chosen = revision is { } n
            ? revisions.FirstOrDefault(r => r.Revision == n)
            : revisions.FirstOrDefault(r => r.IsCurrent);
        if (chosen is null) return null;
        if (!chosen.IsCurrent && !isAdmin)
            throw new ForbiddenException("Revisões anteriores são visíveis só para administradores.");

        var sheet = DeserializeSheet(chosen.SheetJson);
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
            return new RoundTeamDto(t.Id, t.Name, ColorOf(t), s.Played, s.Wins, s.Draws, s.Losses, s.GoalsFor, s.GoalsAgainst, s.Points, s.IsRoundWinner,
                s.WinsRegular, s.WinsPenalties);
        }).ToList();

        var games = sheet.Games.OrderBy(g => g.Order).Select(g =>
        {
            var outcome = g.Status == RoundGameStatus.InProgress
                ? "InProgress"
                : g.HomeScore > g.AwayScore
                    ? "HomeWin"
                    : g.HomeScore < g.AwayScore
                        ? "AwayWin"
                        : g.PenaltyWinnerTeamId == g.HomeTeamId
                            ? "HomePenaltyWin"
                            : g.PenaltyWinnerTeamId == g.AwayTeamId
                                ? "AwayPenaltyWin"
                                : "Draw";

            var saves = (g.PenaltySaves ?? [])
                .Select(s => new RoundPenaltySaveDto(s.GoalkeeperProfileId, Name(s.GoalkeeperProfileId), s.Count))
                .ToList();

            var goalDtos = sheet.Goals.Where(x => x.GameOrder == g.Order)
                .Select(x => new RoundGoalDto(
                    x.PlayerProfileId,
                    Name(x.PlayerProfileId),
                    byPlayer[x.PlayerProfileId].TeamId,
                    x.OwnGoal,
                    Id: x.Id,
                    TeamCreditedId: RoundRules.CreditedTeamId(g, x, byPlayer)))
                .ToList();

            return new RoundGameDto(
                g.Order, g.HomeTeamId, g.AwayTeamId, g.HomeScore, g.AwayScore, outcome, goalDtos,
                Id: g.Id,
                Status: g.Status.ToString(),
                DecidedByPenalties: g.DecidedByPenalties,
                PenaltyWinnerTeamId: g.PenaltyWinnerTeamId,
                WinnerTeamId: g.WinnerTeamId,
                PenaltySaves: saves);
        }).ToList();

        var participants = sheet.Participants
            .OrderBy(p => teamById[p.TeamId].SortOrder).ThenByDescending(p => p.PlayedGoalkeeper).ThenBy(p => Name(p.PlayerProfileId), StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                var line = lines[p.PlayerProfileId];
                return new RoundParticipantDto(
                    p.PlayerProfileId, Name(p.PlayerProfileId), p.TeamId, p.Present, p.PlayedGoalkeeper,
                    line.Goals, line.OwnGoals, line.GamesWon, line.GamesWonOnPenalties, line.PenaltySaves);
            }).ToList();

        var history = isAdmin
            ? revisions.OrderBy(r => r.Revision).Select(r => new RoundRevisionInfoDto(r.Revision, r.IsCurrent, r.CreatedAt, r.Reason)).ToList()
            : [];

        return new RoundResultDto(
            matchId, false, chosen.Revision, chosen.IsCurrent, chosen.CreatedAt, localDate,
            teams, games, participants,
            sheet.BestPlayerId, sheet.BestPlayerId is { } bp ? Name(bp) : null,
            sheet.BestGoalkeeperId, sheet.BestGoalkeeperId is { } bg ? Name(bg) : null,
            sheet.Notes, null, null, null, history, RulesVersion: sheet.RulesVersion);
    }

    public async Task<SeasonStatsDto> GetSeasonAsync(Guid userId, Guid groupId, int? year, int? month, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);
        if (month is < 1 or > 12)
            throw new DomainException("Mês deve ficar entre 1 e 12.");

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
        var today = GroupClock.LocalDate(DateTimeOffset.UtcNow, group.TimeZoneId);
        var y = year ?? today.Year;
        var scorerMonth = month ?? (y == today.Year ? today.Month : (int?)null);

        var completed = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Completed)
            .ToListAsync(ct);
        var inYear = completed
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
            rounds.Add((m, local, rev is null ? null : DeserializeSheet(rev.SheetJson), draw));
        }

        bool InPeriod(DateTime local, int? mth) => mth is null || local.Month == mth;
        var period = rounds.Where(r => InPeriod(r.Local, month)).OrderByDescending(r => r.Local).ToList();

        IEnumerable<(Guid, int)> GoalsOf(IEnumerable<(Match Match, DateTime Local, RoundSheet? Sheet, TeamDraw? Draw)> rs)
            => rs.Where(r => r.Sheet is not null && r.Draw is not null)
                .SelectMany(r => RoundRules.PlayerLines(r.Sheet!, TeamIds(r.Draw!)))
                .Select(l => (l.PlayerProfileId, l.Goals));
        var yearScorers = ScorerTable.Rank(GoalsOf(rounds));
        var monthScorers = scorerMonth is null ? [] : ScorerTable.Rank(GoalsOf(rounds.Where(r => r.Local.Month == scorerMonth)));

        var colors = ThreeColorDraw.Colors.ToDictionary(c => c, _ => new int[8]);
        var players = new Dictionary<Guid, int[]>();
        int[] Row(Guid id) => players.TryGetValue(id, out var r) ? r : players[id] = new int[9];

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
                    acc[6] += s.WinsRegular; acc[7] += s.WinsPenalties;
                }
                foreach (var l in RoundRules.PlayerLines(r.Sheet, teamIds))
                {
                    var row = Row(l.PlayerProfileId);
                    if (l.Present) row[0]++;
                    if (l.RoundWon) row[1]++;
                    row[2] += l.GamesWon; row[3] += l.Goals; row[4] += l.OwnGoals;
                    if (l.BestPlayer) row[5]++;
                    if (l.BestGoalkeeper) row[6]++;
                    row[7] += l.GamesWonOnPenalties;
                    row[8] += l.PenaltySaves;
                }
            }
            else
            {
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
            var games = r.Sheet.Games.OrderBy(g => g.Order).Select(g =>
            {
                var baseText = $"{colorOf[g.HomeTeamId]} {g.HomeScore} x {g.AwayScore} {colorOf[g.AwayTeamId]}";
                if (g.DecidedByPenalties && g.PenaltyWinnerTeamId is { } pw && colorOf.TryGetValue(pw, out var pwColor))
                    return $"{baseText} (Pên.: {pwColor})";
                return baseText;
            }).ToList();
            return new SeasonRoundDto(r.Match.Id, r.Match.Title, local, false, winners, games);
        }).ToList();

        return new SeasonStatsDto(
            y, month, group.TimeZoneId,
            monthScorers.Select(ToDto).ToList(),
            yearScorers.Select(ToDto).ToList(),
            colors.Select(c => new ColorStandingDto(c.Key, c.Value[0], c.Value[1], c.Value[2], c.Value[3], c.Value[4], c.Value[5], c.Value[6], c.Value[7])).ToList(),
            players.Select(p => new SeasonPlayerDto(p.Key, Name(p.Key), p.Value[0], p.Value[1], p.Value[2], p.Value[3], p.Value[4], p.Value[5], p.Value[6], p.Value[7], p.Value[8]))
                .OrderByDescending(p => p.Goals).ThenByDescending(p => p.RoundsWon).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            roundDtos);
    }

    // ---------- auxiliares ----------

    private static RoundSheet DeserializeSheet(string json)
        => JsonSerializer.Deserialize<RoundSheet>(json, Json)
            ?? throw new DomainException("Súmula inválida.");

    private static RoundSheet CreateInitialLiveSheet(TeamDraw draw)
    {
        var participants = draw.Teams
            .SelectMany(t => t.Members.Select(m => new RoundParticipant(m.PlayerProfileId, t.Id, true, m.Role == TeamRole.Goalkeeper)))
            .ToList();
        return new RoundSheet(participants, [], [], null, null, null, RoundRules.CurrentRulesVersion);
    }

    private async Task<(Match Match, TeamDraw Draw, MatchLiveSheet? Live, RoundSheet Sheet)> LoadForLiveMutationAsync(
        Guid userId,
        Guid matchId,
        int expectedVersion,
        bool allowDrawnStart,
        bool checkVersion,
        CancellationToken ct)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new NotFoundException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);

        if (match.Status == MatchStatus.Completed)
            throw new ConflictException("Este dia já foi encerrado. Use a correção auditada com motivo.", "day_completed");
        if (match.Status is MatchStatus.Draft or MatchStatus.RegistrationOpen or MatchStatus.RegistrationClosed or MatchStatus.Cancelled)
            throw new ConflictException("A pelada precisa estar com sorteio confirmado para o modo ao vivo.", "day_not_started");
        if (!allowDrawnStart && match.Status == MatchStatus.Drawn)
            throw new ConflictException("Inicie a primeira partida do dia antes de registrar eventos.", "day_not_started");

        var draw = await LoadColorDrawForLiveAsync(matchId, ct);
        var live = await _db.MatchLiveSheets.FirstOrDefaultAsync(l => l.MatchId == matchId, ct);

        if (checkVersion)
            EnsureExpectedVersion(expectedVersion, live?.Version ?? 0);

        var sheet = live is null ? CreateInitialLiveSheet(draw) : DeserializeSheet(live.SheetJson);
        return (match, draw, live, sheet);
    }

    private static void EnsureExpectedVersion(int expectedVersion, int currentVersion)
    {
        if (expectedVersion != currentVersion)
        {
            throw new ConflictException(
                "O placar foi alterado por outro aparelho. Atualize antes de continuar.",
                "live_version_conflict",
                new Dictionary<string, object?> { ["currentVersion"] = currentVersion });
        }
    }

    private async Task<int> SaveLiveSheetAsync(Match match, MatchLiveSheet? live, RoundSheet sheet, Guid userId, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(sheet, Json);
        if (live is null)
        {
            live = new MatchLiveSheet
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                SheetJson = json,
                Version = 1,
                UpdatedByUserId = userId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.MatchLiveSheets.Add(live);
        }
        else
        {
            live.SheetJson = json;
            live.Version++;
            live.UpdatedByUserId = userId;
            live.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return live.Version;
    }

    private async Task<LiveDayDto> BuildLiveDayDtoAsync(
        Match match,
        string localDate,
        int version,
        TeamDraw draw,
        RoundSheet sheet,
        CancellationToken ct)
    {
        var orderedTeams = draw.Teams.OrderBy(t => t.SortOrder).ToList();
        var teamIds = orderedTeams.Select(t => t.Id).ToList();
        var teamById = orderedTeams.ToDictionary(t => t.Id);
        var byPlayer = sheet.Participants.ToDictionary(p => p.PlayerProfileId);

        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, sheet.Participants.Select(p => p.PlayerProfileId), ct);
        string Name(Guid id) => names.GetValueOrDefault(id, "Jogador");

        var standings = RoundRules.Standings(sheet, teamIds);
        var standingsByTeam = standings.ToDictionary(s => s.TeamId);
        var lines = RoundRules.PlayerLines(sheet, teamIds).ToDictionary(l => l.PlayerProfileId);

        var activeKeepersByTeam = sheet.Participants
            .Where(p => p.Present && p.PlayedGoalkeeper)
            .GroupBy(p => p.TeamId)
            .ToDictionary(g => g.Key, g => g.Count());

        var teamDtos = orderedTeams.Select(t => new LiveTeamDto(t.Id, t.Name, ColorOf(t))).ToList();
        var gameDtos = sheet.Games.OrderBy(g => g.Order).Select(g =>
        {
            var gameGoals = sheet.Goals.Where(x => x.GameOrder == g.Order)
                .Select(x => new LiveGoalDto(
                    x.Id,
                    x.PlayerProfileId,
                    Name(x.PlayerProfileId),
                    x.OwnGoal,
                    RoundRules.CreditedTeamId(g, x, byPlayer)))
                .ToList();
            var saves = (g.PenaltySaves ?? [])
                .Select(s => new RoundPenaltySaveDto(s.GoalkeeperProfileId, Name(s.GoalkeeperProfileId), s.Count))
                .ToList();
            return new LiveGameDto(
                g.Id,
                g.Order,
                g.Status.ToString(),
                g.HomeTeamId,
                g.AwayTeamId,
                g.HomeScore,
                g.AwayScore,
                g.DecidedByPenalties,
                gameGoals,
                g.PenaltyWinnerTeamId,
                g.WinnerTeamId,
                saves);
        }).ToList();

        var standingDtos = orderedTeams.Select(t =>
        {
            var s = standingsByTeam[t.Id];
            return new LiveStandingDto(t.Id, s.Played, s.Wins, s.WinsRegular, s.WinsPenalties, s.Losses, s.GoalsFor, s.GoalsAgainst, s.IsRoundWinner);
        }).ToList();

        var playerDtos = sheet.Participants
            .OrderBy(p => teamById[p.TeamId].SortOrder)
            .ThenByDescending(p => p.PlayedGoalkeeper)
            .ThenBy(p => Name(p.PlayerProfileId), StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                var line = lines[p.PlayerProfileId];
                var teamStanding = standingsByTeam[p.TeamId];
                var soleKeeper = p.Present && p.PlayedGoalkeeper && activeKeepersByTeam.GetValueOrDefault(p.TeamId, 0) == 1;
                return new LivePlayerDto(
                    p.PlayerProfileId,
                    Name(p.PlayerProfileId),
                    p.TeamId,
                    p.Present,
                    p.PlayedGoalkeeper,
                    line.Goals,
                    line.OwnGoals,
                    line.GamesWon,
                    line.GamesWonOnPenalties,
                    line.PenaltySaves,
                    teamStanding.Played > 0 ? teamStanding.GoalsAgainst : null,
                    soleKeeper);
            }).ToList();

        SuggestedNextGameDto? suggestedNext = null;
        if (match.Status != MatchStatus.Completed)
        {
            var next = RoundRules.SuggestNextMatchup(sheet, teamIds);
            if (next is { } pair)
                suggestedNext = new SuggestedNextGameDto(pair.HomeTeamId, pair.AwayTeamId);
        }

        return new LiveDayDto(
            match.Id,
            localDate,
            match.Status.ToString(),
            version,
            teamDtos,
            gameDtos,
            standingDtos,
            playerDtos,
            suggestedNext);
    }

    private async Task<TeamDraw> LoadColorDrawForLiveAsync(Guid matchId, CancellationToken ct)
    {
        var draw = (await _db.TeamDraws.AsNoTracking().Include(d => d.Teams).ThenInclude(t => t.Members)
                .Where(d => d.MatchId == matchId && d.Status == TeamDrawStatus.Confirmed)
                .ToListAsync(ct))
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();
        if (draw is null || draw.AlgorithmVersion is null)
            throw new ConflictException("Confirme o sorteio de três cores antes de usar o modo ao vivo.", "day_not_started");
        return draw;
    }

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

    private static List<RoundGoal> NormalizeLiveGoals(int gameOrder, IReadOnlyList<LiveGoalInput>? inputs, List<RoundParticipant> participants)
    {
        var result = new List<RoundGoal>();
        if (inputs is null || inputs.Count == 0) return result;
        var seen = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            var id = input.GoalId == Guid.Empty ? Guid.NewGuid() : input.GoalId;
            if (!seen.Add(id)) continue;
            var pIdx = participants.FindIndex(p => p.PlayerProfileId == input.PlayerProfileId);
            if (pIdx >= 0 && !participants[pIdx].Present)
                participants[pIdx] = participants[pIdx] with { Present = true };
            result.Add(new RoundGoal(gameOrder, input.PlayerProfileId, input.OwnGoal, id));
        }
        return result;
    }

    private static IReadOnlyList<RoundPenaltySave> NormalizePenaltySaves(IReadOnlyList<RoundPenaltySaveInput>? inputs)
        => inputs is null || inputs.Count == 0
            ? []
            : inputs.Select(s => new RoundPenaltySave(s.GoalkeeperProfileId, s.Count)).ToList();

    private static (int Home, int Away) ComputeScoresSafe(
        RoundGame game,
        IEnumerable<RoundGoal> goals,
        IReadOnlyDictionary<Guid, RoundParticipant> byPlayer)
    {
        int home = 0, away = 0;
        foreach (var goal in goals.Where(g => g.GameOrder == game.Order))
        {
            if (!byPlayer.TryGetValue(goal.PlayerProfileId, out var scorer))
                throw new DomainException("Gol atribuído a quem não está escalado na rodada.", "invalid_game");
            if (scorer.TeamId != game.HomeTeamId && scorer.TeamId != game.AwayTeamId)
                throw new DomainException($"Confronto {game.Order}: o autor do gol não joga em nenhum dos dois times.", "invalid_game");
            var forHome = goal.OwnGoal ? scorer.TeamId == game.AwayTeamId : scorer.TeamId == game.HomeTeamId;
            if (forHome) home++; else away++;
        }
        return (home, away);
    }

    private static List<RoundParticipant> MergePresence(
        IReadOnlyList<RoundParticipant> current,
        IReadOnlyList<RoundPresenceInput> updates,
        TeamDraw draw)
    {
        var members = draw.Teams.SelectMany(t => t.Members.Select(m => (Member: m, TeamId: t.Id))).ToList();
        var memberIds = members.Select(m => m.Member.PlayerProfileId).ToHashSet();
        if (updates.Any(p => !memberIds.Contains(p.PlayerProfileId)))
            throw new DomainException("Presença informada para quem não está no sorteio confirmado.", "invalid_game");
        if (updates.Select(p => p.PlayerProfileId).Distinct().Count() != updates.Count)
            throw new DomainException("Jogador repetido na presença.", "invalid_game");

        var currentByPlayer = current.ToDictionary(p => p.PlayerProfileId);
        var updateByPlayer = updates.ToDictionary(p => p.PlayerProfileId);

        return members.Select(m =>
        {
            var pid = m.Member.PlayerProfileId;
            var existing = currentByPlayer.GetValueOrDefault(pid);
            var upd = updateByPlayer.GetValueOrDefault(pid);
            var present = upd?.Present ?? existing?.Present ?? true;
            var playedGk = upd?.PlayedGoalkeeper ?? existing?.PlayedGoalkeeper ?? (m.Member.Role == TeamRole.Goalkeeper);
            return new RoundParticipant(pid, m.TeamId, present, playedGk);
        }).ToList();
    }

    private static RoundSheet BuildSheet(RoundSheetRequest request, TeamDraw draw)
    {
        var members = draw.Teams.SelectMany(t => t.Members.Select(m => (Member: m, TeamId: t.Id))).ToList();
        var memberIds = members.Select(m => m.Member.PlayerProfileId).ToHashSet();
        var presence = request.Presence ?? [];
        if (presence.Any(p => !memberIds.Contains(p.PlayerProfileId)))
            throw new DomainException("Presença informada para quem não está no sorteio confirmado.", "invalid_game");
        if (presence.Select(p => p.PlayerProfileId).Distinct().Count() != presence.Count)
            throw new DomainException("Jogador repetido na presença.", "invalid_game");

        var participants = members.Select(m =>
        {
            var input = presence.FirstOrDefault(p => p.PlayerProfileId == m.Member.PlayerProfileId);
            return new RoundParticipant(
                m.Member.PlayerProfileId, m.TeamId,
                input?.Present ?? true,
                input?.PlayedGoalkeeper ?? m.Member.Role == TeamRole.Goalkeeper);
        }).ToList();

        var games = (request.Games ?? []).Select((g, i) => new RoundGame(
            Order: i + 1,
            HomeTeamId: g.HomeTeamId,
            AwayTeamId: g.AwayTeamId,
            HomeScore: g.HomeScore,
            AwayScore: g.AwayScore,
            Id: g.Id is { } gid && gid != Guid.Empty ? gid : Guid.NewGuid(),
            Status: RoundGameStatus.Finished,
            PenaltyWinnerTeamId: g.PenaltyWinnerTeamId,
            PenaltySaves: NormalizePenaltySaves(g.PenaltySaves))).ToList();

        var goals = (request.Goals ?? []).Select(g => new RoundGoal(
            g.Game,
            g.PlayerProfileId,
            g.OwnGoal,
            g.Id is { } goalId && goalId != Guid.Empty ? goalId : Guid.NewGuid())).ToList();

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        return new RoundSheet(participants, games, goals, request.BestPlayerProfileId, request.BestGoalkeeperProfileId, notes, RoundRules.CurrentRulesVersion);
    }

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
        result.HomeScore = 0;
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
