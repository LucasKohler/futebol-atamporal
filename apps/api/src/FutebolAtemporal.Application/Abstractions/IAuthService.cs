using FutebolAtemporal.Application.Auth;

namespace FutebolAtemporal.Application.Abstractions;

public interface IAuthService
{
    Task<(bool Succeeded, string? Error)> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<(bool Succeeded, string? Error, string? Code)> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<UserDto?> GetCurrentUserAsync(CancellationToken ct = default);
    /// <summary>Retorna (ok, httpStatus, erro, código). Funciona só enquanto o admin não tem senha.</summary>
    Task<(bool Succeeded, int Status, string? Error, string? Code)> SetupFirstAdminAsync(AdminSetupRequest request, CancellationToken ct = default);
    Task<(bool Succeeded, int Status, string? Error, string? Code)> RecoverAdminAsync(AdminRecoveryRequest request, CancellationToken ct = default);
    Task AdminResetPasswordAsync(Guid adminUserId, Guid groupId, Guid playerProfileId, AdminResetPasswordRequest request, CancellationToken ct = default);
}

public interface IGroupQueryService
{
    Task<GroupDto?> GetCurrentGroupAsync(Guid userId, CancellationToken ct = default);
    Task<GroupDto?> GetByIdAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<GroupPlayerDto>> ListPlayersAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<GroupPlayerDto> SetNicknameAsync(Guid userId, Guid groupId, Guid playerProfileId, string? nickname, CancellationToken ct = default);
    Task<GroupPlayerDto> SetRatingsAsync(Guid userId, Guid groupId, Guid playerProfileId, SetRatingsRequest request, CancellationToken ct = default);
}

public sealed record GroupDto(
    Guid Id,
    string Name,
    string Slug,
    string TimeZoneId,
    string Role);
