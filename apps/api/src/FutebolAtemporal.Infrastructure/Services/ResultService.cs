using FutebolAtemporal.Application.Notifications;
using System.Text.Json;
using System.Text.Json.Serialization;
using FutebolAtemporal.Application.Results;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class ResultService : IResultService
{
    private readonly AppDbContext _db;
    private readonly INotificationTriggerService? _notifications;

    public ResultService(AppDbContext db, INotificationTriggerService? notifications = null)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<MatchResultDto> CloseAsync(Guid userId, Guid matchId, CloseMatchRequest request, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);

        if (!MatchTransitions.CanClose(match.Status))
            throw new DomainException("Partida ainda não pode ser encerrada.");

        if (await _db.MatchResults.AnyAsync(r => r.MatchId == matchId, ct))
            throw new DomainException("Resultado já registrado.");
        if (await _db.TeamDraws.AnyAsync(d => d.MatchId == matchId && d.Status == TeamDrawStatus.Confirmed && d.AlgorithmVersion != null, ct))
            throw new DomainException("Rodada de três cores: registre os confrontos, gols e prêmios pela súmula da rodada.");

        foreach (var p in request.Presence)
        {
            var reg = await _db.MatchRegistrations
                .FirstOrDefaultAsync(r => r.MatchId == matchId && r.PlayerProfileId == p.PlayerProfileId, ct);
            if (reg is null) continue;
            reg.Status = p.Present ? RegistrationStatus.Participated : RegistrationStatus.Absent;
            reg.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var result = new MatchResult
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            HomeScore = request.HomeScore,
            AwayScore = request.AwayScore,
            WinnerTeamId = request.WinnerTeamId,
            Notes = request.Notes,
            ClosedByUserId = userId
        };
        _db.MatchResults.Add(result);

        foreach (var rating in request.Ratings)
        {
            _db.PlayerMatchRatings.Add(new PlayerMatchRating
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                PlayerProfileId = rating.PlayerProfileId,
                Score = rating.Score,
                Notes = rating.Notes,
                RatedByUserId = userId
            });
        }

        if (request.ManOfTheMatchPlayerProfileId.HasValue)
        {
            _db.PlayerAwards.Add(new PlayerAward
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                PlayerProfileId = request.ManOfTheMatchPlayerProfileId.Value,
                AwardedByUserId = userId
            });
        }

        // Diárias nascem no fechamento do dia pelo admin (também no fluxo legado A/B).
        var present = request.Presence.Where(p => p.Present).Select(p => p.PlayerProfileId).ToList();
        var presentRegistered = await _db.MatchRegistrations.AsNoTracking()
            .Where(r => r.MatchId == matchId && present.Contains(r.PlayerProfileId))
            .Select(r => r.PlayerProfileId).ToListAsync(ct);
        await MatchDayFeeLifecycle.LaunchOnCloseAsync(_db, match, presentRegistered, userId, DateTimeOffset.UtcNow, ct);

        match.Status = MatchStatus.Completed;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++;

        if (_notifications is not null)
        {
            var participantIds = await _db.MatchRegistrations
                .Where(r => r.MatchId == matchId && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Participated))
                .Select(r => r.PlayerProfileId)
                .ToListAsync(ct);
            await _notifications.EnqueueMatchCompletedAsync(match.GroupId, match.Id, participantIds, ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _notifications?.SignalDispatcher();

        return new MatchResultDto(matchId, result.HomeScore, result.AwayScore, result.WinnerTeamId,
            request.ManOfTheMatchPlayerProfileId, result.Notes, result.ClosedAt);
    }

    public async Task<MatchResultDto?> GetAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        var match = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return null;
        await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        var result = await _db.MatchResults.AsNoTracking().FirstOrDefaultAsync(r => r.MatchId == matchId, ct);
        if (result is null) return null;
        var award = await _db.PlayerAwards.AsNoTracking()
            .FirstOrDefaultAsync(a => a.MatchId == matchId && a.Type == AwardType.ManOfTheMatch, ct);
        return new MatchResultDto(matchId, result.HomeScore, result.AwayScore, result.WinnerTeamId,
            award?.PlayerProfileId, result.Notes, result.ClosedAt);
    }
}

public sealed class StatsService : IStatsService
{
    private static readonly JsonSerializerOptions SheetJsonOptions = CreateJsonOptions();
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public StatsService(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    public async Task<IReadOnlyList<RankingRowDto>> GetRankingsAsync(Guid userId, Guid groupId, string sortBy, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);

        var completedMatchIds = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Completed)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var regs = await _db.MatchRegistrations.AsNoTracking()
            .Where(r => completedMatchIds.Contains(r.MatchId)
                && (r.Status == RegistrationStatus.Participated || r.Status == RegistrationStatus.Absent))
            .ToListAsync(ct);

        var results = await _db.MatchResults.AsNoTracking()
            .Where(r => completedMatchIds.Contains(r.MatchId))
            .ToListAsync(ct);
        var draws = await _db.TeamDraws.AsNoTracking()
            .Include(d => d.Teams).ThenInclude(t => t.Members)
            .Where(d => completedMatchIds.Contains(d.MatchId) && d.Status == TeamDrawStatus.Confirmed)
            .ToListAsync(ct);
        var revisions = await _db.MatchResultRevisions.AsNoTracking()
            .Where(r => completedMatchIds.Contains(r.MatchId) && r.IsCurrent)
            .ToListAsync(ct);
        var ratings = await _db.PlayerMatchRatings.AsNoTracking()
            .Where(r => completedMatchIds.Contains(r.MatchId))
            .ToListAsync(ct);
        var awards = await _db.PlayerAwards.AsNoTracking()
            .Where(a => completedMatchIds.Contains(a.MatchId))
            .ToListAsync(ct);

        var penaltySavesByPlayer = new Dictionary<Guid, int>();
        foreach (var rev in revisions)
        {
            var draw = draws.FirstOrDefault(d => d.MatchId == rev.MatchId);
            if (draw is null) continue;
            var sheet = JsonSerializer.Deserialize<RoundSheet>(rev.SheetJson, SheetJsonOptions);
            if (sheet is null) continue;
            var teamIds = draw.Teams.Select(t => t.Id).ToList();
            foreach (var line in RoundRules.PlayerLines(sheet, teamIds))
            {
                if (line.PenaltySaves > 0)
                    penaltySavesByPlayer[line.PlayerProfileId] = penaltySavesByPlayer.GetValueOrDefault(line.PlayerProfileId) + line.PenaltySaves;
            }
        }

        var profileIds = regs.Select(r => r.PlayerProfileId)
            .Concat(penaltySavesByPlayer.Keys)
            .Distinct()
            .ToList();
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, profileIds, ct);

        var stats = new List<RankingCalculator.PlayerStats>();
        foreach (var pid in profileIds)
        {
            var played = regs.Where(r => r.PlayerProfileId == pid).ToList();
            var participated = played.Where(r => r.Status == RegistrationStatus.Participated).ToList();
            var absences = played.Count(r => r.Status == RegistrationStatus.Absent);
            var wins = 0;
            var losses = 0;
            foreach (var reg in participated)
            {
                var result = results.FirstOrDefault(r => r.MatchId == reg.MatchId);
                if (result?.WinnerTeamId is null) continue;
                var draw = draws.FirstOrDefault(d => d.MatchId == reg.MatchId);
                var team = draw?.Teams.FirstOrDefault(t => t.Members.Any(m => m.PlayerProfileId == pid));
                if (team is null) continue;
                if (team.Id == result.WinnerTeamId) wins++;
                else losses++;
            }

            var playerRatings = ratings.Where(r => r.PlayerProfileId == pid).Select(r => r.Score).ToList();
            var matchesCount = participated.Count;
            var winRate = matchesCount == 0 ? 0 : (decimal)wins / matchesCount;
            stats.Add(new RankingCalculator.PlayerStats(
                pid, names.GetValueOrDefault(pid, "Jogador"), matchesCount, wins, losses, absences,
                awards.Count(a => a.PlayerProfileId == pid && a.Type == AwardType.ManOfTheMatch),
                playerRatings.Count == 0 ? null : playerRatings.Average(),
                winRate,
                penaltySavesByPlayer.GetValueOrDefault(pid, 0),
                awards.Count(a => a.PlayerProfileId == pid && a.Type == AwardType.BestGoalkeeper)));
        }

        var ordered = RankingCalculator.Order(stats, sortBy);

        return ordered.Select(s => new RankingRowDto(
            s.PlayerProfileId, s.DisplayName, s.Matches, s.Wins, s.Losses, s.Absences,
            s.ManOfTheMatch, s.AverageRating, s.WinRate, s.PenaltySaves, s.BestGoalkeeper)).ToList();
    }

    public async Task<object?> GetPlayerHistoryAsync(Guid userId, Guid playerProfileId, CancellationToken ct = default)
    {
        var profile = await _db.PlayerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerProfileId, ct);
        if (profile is null) return null;
        var membership = await _db.GroupMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Status == MembershipStatus.Active, ct);
        if (membership is null) throw new DomainException("Sem grupo.");

        var matchIds = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == membership.GroupId && m.Status == MatchStatus.Completed)
            .Select(m => m.Id).ToListAsync(ct);
        var regs = await _db.MatchRegistrations.AsNoTracking()
            .Where(r => r.PlayerProfileId == playerProfileId && matchIds.Contains(r.MatchId))
            .ToListAsync(ct);
        var matches = await _db.Matches.AsNoTracking().Where(m => regs.Select(r => r.MatchId).Contains(m.Id)).ToListAsync(ct);
        var ratings = await _db.PlayerMatchRatings.AsNoTracking()
            .Where(r => r.PlayerProfileId == playerProfileId && matchIds.Contains(r.MatchId)).ToListAsync(ct);
        var revisions = await _db.MatchResultRevisions.AsNoTracking()
            .Where(r => matchIds.Contains(r.MatchId) && r.IsCurrent).ToListAsync(ct);
        var draws = await _db.TeamDraws.AsNoTracking()
            .Include(d => d.Teams)
            .Where(d => matchIds.Contains(d.MatchId) && d.Status == TeamDrawStatus.Confirmed).ToListAsync(ct);
        var bestGoalkeeperCount = await _db.PlayerAwards.AsNoTracking()
            .CountAsync(a => matchIds.Contains(a.MatchId) && a.PlayerProfileId == playerProfileId && a.Type == AwardType.BestGoalkeeper, ct);

        var lineByMatch = new Dictionary<Guid, PlayerRoundLine>();
        foreach (var rev in revisions)
        {
            var draw = draws.FirstOrDefault(d => d.MatchId == rev.MatchId);
            if (draw is null) continue;
            var sheet = JsonSerializer.Deserialize<RoundSheet>(rev.SheetJson, SheetJsonOptions);
            if (sheet is null) continue;
            var line = RoundRules.PlayerLines(sheet, draw.Teams.Select(t => t.Id).ToList())
                .FirstOrDefault(l => l.PlayerProfileId == playerProfileId);
            if (line is not null)
                lineByMatch[rev.MatchId] = line;
        }

        var totalPenaltySaves = lineByMatch.Values.Sum(l => l.PenaltySaves);
        var name = await AccessHelper.DisplayNameAsync(_users, profile.UserId);

        return new
        {
            playerProfileId,
            displayName = name,
            skillRating = membership.Role == GroupRole.Admin ? profile.SkillRating : (decimal?)null,
            penaltySaves = totalPenaltySaves,
            bestGoalkeeper = bestGoalkeeperCount,
            history = matches.OrderByDescending(m => m.StartsAt).Select(m =>
            {
                var reg = regs.First(r => r.MatchId == m.Id);
                var rating = ratings.FirstOrDefault(r => r.MatchId == m.Id);
                var line = lineByMatch.GetValueOrDefault(m.Id);
                return new
                {
                    matchId = m.Id,
                    title = m.Title,
                    startsAt = m.StartsAt,
                    location = m.Location,
                    status = reg.Status.ToString(),
                    rating = rating?.Score,
                    playedGoalkeeper = line?.PlayedGoalkeeper ?? false,
                    penaltySaves = line?.PenaltySaves ?? 0
                };
            })
        };
    }
}
