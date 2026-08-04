using FutebolAtemporal.Application.Matches;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class MatchService : IMatchService
{
    private readonly AppDbContext _db;

    public MatchService(AppDbContext db) => _db = db;

    public async Task<MatchDto> CreateAsync(Guid userId, CreateMatchRequest request, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, request.GroupId, ct, adminOnly: true);
        if (request.PlayerLimit < 2)
            throw new DomainException("Limite de jogadores deve ser pelo menos 2.");

        var match = new Match
        {
            Id = Guid.NewGuid(),
            GroupId = request.GroupId,
            Title = request.Title,
            StartsAt = request.StartsAt,
            Location = request.Location.Trim(),
            PlayerLimit = request.PlayerLimit,
            Status = request.OpenRegistrations ? MatchStatus.RegistrationOpen : MatchStatus.Draft
        };
        _db.Matches.Add(match);
        await _db.SaveChangesAsync(ct);
        return ToDto(match, 0, 0);
    }

    public async Task<MatchDto> UpdateAsync(Guid userId, Guid matchId, UpdateMatchRequest request, CancellationToken ct = default)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        if (match.Status is MatchStatus.Completed or MatchStatus.Cancelled)
            throw new DomainException("Partida encerrada não pode ser editada.");

        match.Title = request.Title;
        match.StartsAt = request.StartsAt;
        match.Location = request.Location.Trim();
        match.PlayerLimit = request.PlayerLimit;
        match.Notes = request.Notes;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++;
        await _db.SaveChangesAsync(ct);
        return await ToDtoAsync(match, ct);
    }

    public async Task<MatchDto> TransitionAsync(Guid userId, Guid matchId, MatchStatus target, CancellationToken ct = default)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        MatchTransitions.EnsureTransition(match.Status, target);
        match.Status = target;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++;
        await _db.SaveChangesAsync(ct);
        return await ToDtoAsync(match, ct);
    }

    public async Task<MatchDetailDto?> GetAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        var match = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return null;
        await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        return await BuildDetailAsync(userId, match, ct);
    }

    public async Task<MatchDetailDto?> GetUpcomingAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);
        var now = DateTimeOffset.UtcNow;
        var candidates = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status != MatchStatus.Cancelled && m.Status != MatchStatus.Completed)
            .ToListAsync(ct);
        var match = candidates
            .Where(m => m.StartsAt >= now.AddHours(-6))
            .OrderBy(m => m.StartsAt)
            .FirstOrDefault()
            ?? candidates.Where(m => m.Status == MatchStatus.RegistrationOpen).OrderBy(m => m.Id).FirstOrDefault();

        if (match is null) return null;
        return await BuildDetailAsync(userId, match, ct);
    }

    public async Task<IReadOnlyList<MatchDto>> ListAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);
        var matches = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .ToListAsync(ct);
        matches = matches.OrderByDescending(m => m.StartsAt).Take(50).ToList();

        var result = new List<MatchDto>();
        foreach (var m in matches)
            result.Add(await ToDtoAsync(m, ct));
        return result;
    }

    private async Task<MatchDetailDto> BuildDetailAsync(Guid userId, Match match, CancellationToken ct)
    {
        var regs = await _db.MatchRegistrations.AsNoTracking()
            .Where(r => r.MatchId == match.Id &&
                        (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Waitlisted))
            .ToListAsync(ct);

        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, regs.Select(r => r.PlayerProfileId), ct);
        var skills = await _db.PlayerProfiles.AsNoTracking()
            .Where(p => regs.Select(r => r.PlayerProfileId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.SkillRating, ct);

        RegistrationPlayerDto Map(MatchRegistration r) => new(
            r.Id, r.PlayerProfileId, names.GetValueOrDefault(r.PlayerProfileId, "Jogador"),
            r.Status.ToString(), r.PriorityClass.ToString(), r.WaitlistPosition,
            skills.GetValueOrDefault(r.PlayerProfileId, 5m));

        var confirmed = regs.Where(r => r.Status == RegistrationStatus.Confirmed)
            .OrderBy(r => r.ConfirmedAt?.UtcTicks ?? 0).ThenBy(r => r.Id).Select(Map).ToList();
        var waitlist = regs.Where(r => r.Status == RegistrationStatus.Waitlisted)
            .OrderBy(r => r.WaitlistPosition).ThenBy(r => r.Id).Select(Map).ToList();

        var profile = await _db.PlayerProfiles.AsNoTracking().FirstAsync(p => p.UserId == userId, ct);
        var mine = regs.FirstOrDefault(r => r.PlayerProfileId == profile.Id);

        return new MatchDetailDto(
            ToDto(match, confirmed.Count, waitlist.Count),
            mine?.Status.ToString(),
            mine?.WaitlistPosition,
            confirmed,
            waitlist);
    }

    private async Task<MatchDto> ToDtoAsync(Match match, CancellationToken ct)
    {
        var confirmed = await _db.MatchRegistrations.CountAsync(r => r.MatchId == match.Id && r.Status == RegistrationStatus.Confirmed, ct);
        var wait = await _db.MatchRegistrations.CountAsync(r => r.MatchId == match.Id && r.Status == RegistrationStatus.Waitlisted, ct);
        return ToDto(match, confirmed, wait);
    }

    private static MatchDto ToDto(Match match, int confirmed, int wait) => new(
        match.Id, match.GroupId, match.Title, match.StartsAt, match.Location, match.PlayerLimit,
        match.Status.ToString(), match.Notes, confirmed, wait, Math.Max(0, match.PlayerLimit - confirmed));
}
