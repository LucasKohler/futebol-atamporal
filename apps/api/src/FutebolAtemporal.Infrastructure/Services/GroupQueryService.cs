using FutebolAtemporal.Application.Abstractions;
using FutebolAtemporal.Domain.Enums;
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
}
