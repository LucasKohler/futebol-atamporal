namespace FutebolAtemporal.Infrastructure.Identity;

/// <summary>
/// Sessão de login no servidor. O cookie <c>fa_auth</c> só carrega o id (claim <c>fa_sid</c>);
/// a sessão vale enquanto a linha existir, não estiver revogada nem expirada e o carimbo de segurança
/// do usuário for o mesmo do login. Logout revoga a linha: um cookie antigo reinstalado por uma
/// resposta tardia deixa de autenticar.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>Carimbo de segurança do usuário no login. Diferente do atual = sessão encerrada (reset/recuperação).</summary>
    public string? SecurityStamp { get; set; }
}
