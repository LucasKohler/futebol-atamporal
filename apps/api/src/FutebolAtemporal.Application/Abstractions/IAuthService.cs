using FutebolAtemporal.Application.Auth;

namespace FutebolAtemporal.Application.Abstractions;

public interface IAuthService
{
    Task<(bool Succeeded, string? Error)> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<(bool Succeeded, string? Error)> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<UserDto?> GetCurrentUserAsync(CancellationToken ct = default);
}

public interface IGroupQueryService
{
    Task<GroupDto?> GetCurrentGroupAsync(Guid userId, CancellationToken ct = default);
    Task<GroupDto?> GetByIdAsync(Guid groupId, Guid userId, CancellationToken ct = default);
}

public sealed record GroupDto(
    Guid Id,
    string Name,
    string Slug,
    string TimeZoneId,
    string Role);
