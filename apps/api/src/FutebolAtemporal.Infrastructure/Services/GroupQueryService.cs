using FutebolAtemporal.Application.Abstractions;
using FutebolAtemporal.Application.Auth;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class GroupQueryService : IGroupQueryService
{
    private readonly AppDbContext _db;

    public GroupQueryService(AppDbContext db) => _db = db;

    public async Task<GroupDto?> GetCurrentGroupAsync(Guid userId, CancellationToken ct = default)
    {
        var membership = await _db.GroupMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .OrderBy(m => m.Id)
            .Select(m => new { m.GroupId, m.Role })
            .FirstOrDefaultAsync(ct);

        if (membership is null)
            return null;

        var group = await _db.FootballGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == membership.GroupId, ct);
        if (group is null)
            return null;

        return new GroupDto(group.Id, group.Name, group.Slug, group.TimeZoneId, membership.Role.ToString());
    }

    public async Task<GroupDto?> GetByIdAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var membership = await _db.GroupMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId && m.Status == MembershipStatus.Active, ct);
        if (membership is null)
            return null;

        var group = await _db.FootballGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
            return null;

        return new GroupDto(group.Id, group.Name, group.Slug, group.TimeZoneId, membership.Role.ToString());
    }

    public async Task<IReadOnlyList<GroupPlayerDto>> ListPlayersAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);

        var monthly = await _db.MonthlyMemberships.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MonthlyMembershipStatus.Active)
            .Select(m => m.PlayerProfileId)
            .ToListAsync(ct);
        var monthlySet = monthly.ToHashSet();

        var rows = await (
            from gm in _db.GroupMemberships.AsNoTracking()
            join u in _db.Users.AsNoTracking() on gm.UserId equals u.Id
            join p in _db.PlayerProfiles.AsNoTracking() on u.Id equals p.UserId
            where gm.GroupId == groupId && gm.Status == MembershipStatus.Active
            orderby u.DisplayName
            select new { p.Id, u.DisplayName, u.Nickname, Phone = u.UserName ?? p.Phone ?? string.Empty }
        ).ToListAsync(ct);

        return rows.Select(r => new GroupPlayerDto(
            r.Id,
            r.DisplayName,
            r.Nickname,
            PlayerDisplayName.Effective(r.DisplayName, r.Nickname),
            r.Phone,
            monthlySet.Contains(r.Id))).ToList();
    }

    public async Task<GroupPlayerDto> SetNicknameAsync(Guid userId, Guid groupId, Guid playerProfileId, string? nickname, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);

        var profile = await _db.PlayerProfiles.FirstOrDefaultAsync(p => p.Id == playerProfileId, ct)
            ?? throw new DomainException("Jogador não encontrado.");

        var inGroup = await _db.GroupMemberships.AnyAsync(
            m => m.GroupId == groupId && m.UserId == profile.UserId && m.Status == MembershipStatus.Active, ct);
        if (!inGroup)
            throw new ForbiddenException("Jogador não pertence a este grupo.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == profile.UserId, ct)
            ?? throw new DomainException("Usuário não encontrado.");

        var trimmed = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();
        if (trimmed is { Length: > 80 })
            throw new DomainException("Apelido deve ter no máximo 80 caracteres.");

        user.Nickname = trimmed;
        await _db.SaveChangesAsync(ct);

        var isMonthly = await _db.MonthlyMemberships.AnyAsync(
            m => m.GroupId == groupId && m.PlayerProfileId == playerProfileId && m.Status == MonthlyMembershipStatus.Active, ct);

        return new GroupPlayerDto(
            profile.Id,
            user.DisplayName,
            user.Nickname,
            PlayerDisplayName.Effective(user.DisplayName, user.Nickname),
            user.UserName ?? profile.Phone ?? string.Empty,
            isMonthly);
    }
}
