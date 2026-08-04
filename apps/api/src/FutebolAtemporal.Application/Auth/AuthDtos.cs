namespace FutebolAtemporal.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    Guid? PlayerProfileId,
    IReadOnlyList<MembershipDto> Memberships);

public sealed record MembershipDto(
    Guid GroupId,
    string GroupName,
    string GroupSlug,
    string Role,
    string Status);
