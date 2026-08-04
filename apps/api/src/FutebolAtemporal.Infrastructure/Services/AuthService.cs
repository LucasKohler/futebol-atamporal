using System.Security.Claims;
using FutebolAtemporal.Application.Abstractions;
using FutebolAtemporal.Application.Auth;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<(bool Succeeded, string? Error)> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            return (false, "Email e senha são obrigatórios.");

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
            return (false, "Já existe uma conta com este email.");

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName.Trim(),
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return (false, string.Join(" ", result.Errors.Select(e => e.Description)));

        var profile = new PlayerProfile { Id = Guid.NewGuid(), UserId = user.Id };
        _db.PlayerProfiles.Add(profile);

        var defaultGroup = await _db.FootballGroups.OrderBy(g => g.Id).FirstOrDefaultAsync(ct);
        if (defaultGroup is not null)
        {
            _db.GroupMemberships.Add(new GroupMembership
            {
                Id = Guid.NewGuid(),
                GroupId = defaultGroup.Id,
                UserId = user.Id,
                Role = GroupRole.Player,
                Status = MembershipStatus.Active
            });
        }

        await _db.SaveChangesAsync(ct);
        await _signInManager.SignInAsync(user, isPersistent: true);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
            return (false, "Credenciais inválidas.");

        var result = await _signInManager.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
        if (!result.Succeeded)
            return (false, "Credenciais inválidas.");

        return (true, null);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await _signInManager.SignOutAsync();
    }

    public async Task<UserDto?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
            return null;

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return null;

        var profile = await _db.PlayerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var memberships = await _db.GroupMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Join(_db.FootballGroups, m => m.GroupId, g => g.Id, (m, g) => new MembershipDto(
                g.Id, g.Name, g.Slug, m.Role.ToString(), m.Status.ToString()))
            .ToListAsync(ct);

        return new UserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, profile?.Id, memberships);
    }
}
