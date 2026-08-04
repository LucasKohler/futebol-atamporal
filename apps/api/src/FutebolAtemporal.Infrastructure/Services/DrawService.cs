using FutebolAtemporal.Application.Draws;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class DrawService : IDrawService
{
    private readonly AppDbContext _db;

    public DrawService(AppDbContext db) => _db = db;

    public async Task<DrawDto> CreateDrawAsync(Guid userId, Guid matchId, string? seed, CancellationToken ct = default)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);

        if (match.Status is not (MatchStatus.RegistrationClosed or MatchStatus.RegistrationOpen or MatchStatus.Drawn))
            throw new DomainException("Partida não está pronta para sorteio.");

        var confirmed = await _db.MatchRegistrations
            .Where(r => r.MatchId == matchId && r.Status == RegistrationStatus.Confirmed)
            .Select(r => r.PlayerProfileId)
            .ToListAsync(ct);
        if (confirmed.Count < 2)
            throw new DomainException("É preciso ao menos 2 confirmados para sortear.");

        var profiles = await _db.PlayerProfiles.Where(p => confirmed.Contains(p.Id)).ToListAsync(ct);
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, confirmed, ct);
        var players = profiles.Select(p => new DrawPlayer(p.Id, p.SkillRating, names.GetValueOrDefault(p.Id, "Jogador"))).ToList();

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var teamCount = GroupSettings.Parse(group.SettingsJson).TeamCount;
        var result = BalancedTeamDraw.Draw(players, teamCount, seed);

        var previous = await _db.TeamDraws.Where(d => d.MatchId == matchId && d.Status != TeamDrawStatus.Superseded).ToListAsync(ct);
        foreach (var p in previous)
            p.Status = TeamDrawStatus.Superseded;

        var draw = new TeamDraw
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Seed = result.Seed,
            Status = TeamDrawStatus.Draft,
            TeamCount = teamCount,
            CreatedByUserId = userId
        };

        var order = 0;
        foreach (var team in result.Teams)
        {
            var mt = new MatchTeam
            {
                Id = Guid.NewGuid(),
                TeamDrawId = draw.Id,
                Name = team.Name,
                SortOrder = order++
            };
            foreach (var member in team.Players)
            {
                mt.Members.Add(new MatchTeamMember
                {
                    Id = Guid.NewGuid(),
                    MatchTeamId = mt.Id,
                    PlayerProfileId = member.PlayerProfileId,
                    SkillRatingSnapshot = member.SkillRating
                });
            }
            draw.Teams.Add(mt);
        }

        _db.TeamDraws.Add(draw);
        if (match.Status == MatchStatus.RegistrationOpen)
            match.Status = MatchStatus.RegistrationClosed;

        await _db.SaveChangesAsync(ct);
        return await MapAsync(draw.Id, ct);
    }

    public async Task<DrawDto> ConfirmDrawAsync(Guid userId, Guid drawId, CancellationToken ct = default)
    {
        var draw = await _db.TeamDraws.Include(d => d.Teams).ThenInclude(t => t.Members)
            .FirstOrDefaultAsync(d => d.Id == drawId, ct)
            ?? throw new DomainException("Sorteio não encontrado.");
        var match = await _db.Matches.FirstAsync(m => m.Id == draw.MatchId, ct);
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);

        draw.Status = TeamDrawStatus.Confirmed;
        draw.ConfirmedAt = DateTimeOffset.UtcNow;

        if (match.Status == MatchStatus.RegistrationOpen)
        {
            MatchTransitions.EnsureTransition(match.Status, MatchStatus.RegistrationClosed);
            match.Status = MatchStatus.RegistrationClosed;
        }
        if (match.Status == MatchStatus.RegistrationClosed)
        {
            MatchTransitions.EnsureTransition(match.Status, MatchStatus.Drawn);
            match.Status = MatchStatus.Drawn;
        }
        else if (match.Status != MatchStatus.Drawn)
            throw new DomainException($"Não é possível confirmar sorteio no status {match.Status}.");

        await _db.SaveChangesAsync(ct);
        return await MapAsync(drawId, ct);
    }

    public async Task<DrawDto?> GetCurrentAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        var match = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return null;
        await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        var draws = await _db.TeamDraws.AsNoTracking()
            .Where(d => d.MatchId == matchId && d.Status != TeamDrawStatus.Superseded)
            .ToListAsync(ct);
        var draw = draws.OrderByDescending(d => d.CreatedAt).FirstOrDefault();
        if (draw is null) return null;
        return await MapAsync(draw.Id, ct);
    }

    public async Task SwapPlayersAsync(Guid userId, Guid drawId, Guid playerA, Guid playerB, CancellationToken ct = default)
    {
        var draw = await _db.TeamDraws.Include(d => d.Teams).ThenInclude(t => t.Members)
            .FirstOrDefaultAsync(d => d.Id == drawId, ct)
            ?? throw new DomainException("Sorteio não encontrado.");
        var match = await _db.Matches.FirstAsync(m => m.Id == draw.MatchId, ct);
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        if (draw.Status == TeamDrawStatus.Superseded)
            throw new DomainException("Sorteio inválido.");

        var ma = draw.Teams.SelectMany(t => t.Members).FirstOrDefault(m => m.PlayerProfileId == playerA)
            ?? throw new DomainException("Jogador A não está no sorteio.");
        var mb = draw.Teams.SelectMany(t => t.Members).FirstOrDefault(m => m.PlayerProfileId == playerB)
            ?? throw new DomainException("Jogador B não está no sorteio.");
        if (ma.MatchTeamId == mb.MatchTeamId)
            throw new DomainException("Jogadores já estão no mesmo time.");

        (ma.MatchTeamId, mb.MatchTeamId) = (mb.MatchTeamId, ma.MatchTeamId);
        draw.Status = TeamDrawStatus.Draft;
        draw.ConfirmedAt = null;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<DrawDto> MapAsync(Guid drawId, CancellationToken ct)
    {
        var draw = await _db.TeamDraws.AsNoTracking()
            .Include(d => d.Teams).ThenInclude(t => t.Members)
            .FirstAsync(d => d.Id == drawId, ct);
        var profileIds = draw.Teams.SelectMany(t => t.Members).Select(m => m.PlayerProfileId).ToList();
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, profileIds, ct);
        var teams = draw.Teams.OrderBy(t => t.SortOrder).Select(t =>
        {
            var members = t.Members.Select(m => new DrawMemberDto(
                m.PlayerProfileId, names.GetValueOrDefault(m.PlayerProfileId, "Jogador"), m.SkillRatingSnapshot)).ToList();
            return new DrawTeamDto(t.Id, t.Name, t.SortOrder, members.Sum(x => x.SkillRating), members);
        }).ToList();
        var delta = teams.Count == 0 ? 0 : teams.Max(t => t.TotalSkill) - teams.Min(t => t.TotalSkill);
        return new DrawDto(draw.Id, draw.MatchId, draw.Seed, draw.Status.ToString(), draw.TeamCount, delta, draw.CreatedAt, teams);
    }
}
