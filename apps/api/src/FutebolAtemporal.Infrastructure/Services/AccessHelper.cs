using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

internal static class AccessHelper
{
    public static async Task<(GroupMembership Membership, PlayerProfile Profile)> RequireMemberAsync(
        AppDbContext db, Guid userId, Guid groupId, CancellationToken ct, bool adminOnly = false)
    {
        var membership = await db.GroupMemberships.FirstOrDefaultAsync(
            m => m.UserId == userId && m.GroupId == groupId && m.Status == MembershipStatus.Active, ct)
            ?? throw new ForbiddenException("Você não é membro ativo deste grupo.");

        if (adminOnly && membership.Role != GroupRole.Admin)
            throw new ForbiddenException("Apenas administradores podem executar esta ação.");

        var profile = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new DomainException("Perfil de jogador não encontrado.");

        return (membership, profile);
    }

    public static async Task RequireAdminForMatchAsync(AppDbContext db, Guid userId, Match match, CancellationToken ct)
        => await RequireMemberAsync(db, userId, match.GroupId, ct, adminOnly: true);

    public static async Task<string> DisplayNameAsync(UserManager<ApplicationUser> users, Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user?.DisplayName ?? user?.Email ?? "Jogador";
    }

    public static async Task<Dictionary<Guid, string>> DisplayNamesForProfilesAsync(
        AppDbContext db, IEnumerable<Guid> profileIds, CancellationToken ct)
    {
        var ids = profileIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        var rows = await (
            from p in db.PlayerProfiles.AsNoTracking()
            join u in db.Users.AsNoTracking() on p.UserId equals u.Id
            where ids.Contains(p.Id)
            select new { p.Id, u.DisplayName, u.Email }
        ).ToListAsync(ct);

        return rows.ToDictionary(
            r => r.Id,
            r => string.IsNullOrWhiteSpace(r.DisplayName) ? (r.Email ?? "Jogador") : r.DisplayName);
    }
}
