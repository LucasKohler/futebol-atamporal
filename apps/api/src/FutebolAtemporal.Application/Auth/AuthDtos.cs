namespace FutebolAtemporal.Application.Auth;

public sealed record RegisterRequest(string Phone, string DisplayName, string? Password);
public sealed record LoginRequest(string Phone, string? Password);
/// <summary>Reset feito pelo admin do grupo: define a nova senha do jogador.</summary>
public sealed record AdminResetPasswordRequest(string? NewPassword);
/// <summary>Criação única da senha do primeiro admin, autorizada pelo código de configuração do servidor.</summary>
public sealed record AdminSetupRequest(string? SetupCode, string Phone, string? Password);
/// <summary>Recuperação do único admin com código temporário de uso único configurado pelo operador.</summary>
public sealed record AdminRecoveryRequest(string? RecoveryCode, string Phone, string? NewPassword);
public sealed record UpdateProfileRequest(string? DisplayName, string? Nickname);

public sealed record UserDto(
    Guid Id,
    string Phone,
    string RegisteredName,
    string? Nickname,
    string DisplayName,
    Guid? PlayerProfileId,
    IReadOnlyList<MembershipDto> Memberships);

public sealed record MembershipDto(
    Guid GroupId,
    string GroupName,
    string GroupSlug,
    string Role,
    string Status);

public sealed record GroupPlayerDto(
    Guid PlayerProfileId,
    string RegisteredName,
    string? Nickname,
    string DisplayName,
    string Phone,
    bool IsMonthlyMember,
    decimal LineRating,
    bool CanPlayGoalkeeper,
    decimal? GoalkeeperRating);

public sealed record SetNicknameRequest(string? Nickname);

/// <summary>
/// Notas 0–10. GoalkeeperRating nulo = sem nota de goleiro (diferente de zero).
/// Para desmarcar CanPlayGoalkeeper, envie também GoalkeeperRating = null; caso contrário a API recusa.
/// </summary>
public sealed record SetRatingsRequest(decimal LineRating, bool CanPlayGoalkeeper, decimal? GoalkeeperRating);
