using System.Security.Claims;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FutebolAtemporal.Infrastructure.Identity;

/// <summary>
/// Cookie de sessão sem renovação automática e com revogação no servidor.
/// <list type="bullet">
/// <item>Só login, cadastro, setup e recuperação emitem <c>fa_auth</c> (SignIn). Respostas comuns nunca
/// trazem Set-Cookie: sem SlidingExpiration e sem ShouldRenew, uma resposta tardia da conta A não reinstala A.</item>
/// <item>Toda requisição confere a sessão (<c>fa_sid</c>) e o carimbo de segurança: reset/recuperação de senha
/// e logout encerram sessões antigas na hora.</item>
/// <item>Rejeição não emite Set-Cookie de remoção: uma resposta tardia não apaga o cookie de outra conta.</item>
/// </list>
/// </summary>
public static class SessionCookieEvents
{
    public const string SessionClaimType = "fa_sid";

    /// <summary>Propósito do Data Protection do cookie v2 (diferente do padrão do Identity usado pela v1).</summary>
    public const string TicketPurpose = "FutebolAtemporal.AuthCookie.v2";

    public static Guid? GetSessionId(ClaimsPrincipal? principal)
        => Guid.TryParse(principal?.FindFirstValue(SessionClaimType), out var id) ? id : null;

    /// <summary>Cria a sessão no servidor e grava o id dela no cookie que está sendo emitido.</summary>
    public static async Task OnSigningIn(CookieSigningInContext context)
    {
        var identity = context.Principal?.Identity as ClaimsIdentity;
        if (identity is null || !Guid.TryParse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return;

        var now = context.Properties.IssuedUtc ?? DateTimeOffset.UtcNow;
        var expires = context.Properties.ExpiresUtc ?? now.Add(context.Options.ExpireTimeSpan);
        context.Properties.ExpiresUtc = expires;
        context.Properties.AllowRefresh = false;

        var sessionId = Guid.NewGuid();
        // Escopo próprio: não mistura com alterações pendentes do serviço que chamou o SignIn.
        using var scope = context.HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stampClaimType = context.HttpContext.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
        db.UserSessions.Add(new UserSession
        {
            Id = sessionId, UserId = userId, CreatedAt = now, ExpiresAt = expires,
            SecurityStamp = identity.FindFirst(stampClaimType)?.Value
        });

        // Novo login no mesmo navegador encerra a sessão anterior que ele carregava (se houver),
        // e com ela as inscrições push gravadas por aquela sessão.
        var previous = GetSessionId(context.HttpContext.User);
        if (previous is { } prev)
            await RevokeTrackedAsync(db, prev, now, context.HttpContext.RequestAborted);

        await db.SaveChangesAsync(context.HttpContext.RequestAborted);

        foreach (var c in identity.FindAll(SessionClaimType).ToList()) identity.RemoveClaim(c);
        identity.AddClaim(new Claim(SessionClaimType, sessionId.ToString()));
    }

    /// <summary>Valida sessão e carimbo em toda requisição, sem nunca renovar nem apagar o cookie.</summary>
    public static async Task OnValidatePrincipal(CookieValidatePrincipalContext context)
    {
        context.ShouldRenew = false;
        var principal = context.Principal;
        var sessionId = GetSessionId(principal);
        if (principal is null || sessionId is null
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var ct = context.HttpContext.RequestAborted;
        var session = await db.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId.Value, ct);
        var stampClaimType = services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
        var cookieStamp = principal.FindFirstValue(stampClaimType);
        var currentStamp = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstOrDefaultAsync(ct);
        if (session is null || session.UserId != userId
            || !IsActive(session, currentStamp, DateTimeOffset.UtcNow)
            || !string.Equals(cookieStamp, currentStamp, StringComparison.Ordinal))
            context.RejectPrincipal();
    }

    /// <summary>
    /// Regra única de sessão válida, usada pela API e pelo despachante de push: não revogada, não expirada
    /// e com o mesmo carimbo de segurança do login.
    /// </summary>
    public static bool IsActive(UserSession session, string? currentStamp, DateTimeOffset now)
        => session.RevokedAt is null
            && session.ExpiresAt > now
            && currentStamp is not null
            && string.Equals(session.SecurityStamp, currentStamp, StringComparison.Ordinal);

    /// <summary>Ids, entre <paramref name="sessionIds"/>, das sessões do usuário que ainda valem.</summary>
    public static async Task<HashSet<Guid>> ActiveSessionIdsAsync(AppDbContext db, Guid userId, IEnumerable<Guid> sessionIds, CancellationToken ct)
    {
        var ids = sessionIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var stamp = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstOrDefaultAsync(ct);
        var sessions = await db.UserSessions.AsNoTracking().Where(s => s.UserId == userId && ids.Contains(s.Id)).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        return sessions.Where(s => IsActive(s, stamp, now)).Select(s => s.Id).ToHashSet();
    }

    /// <summary>Revoga a sessão do cookie atual e desativa as inscrições push gravadas por ela. Idempotente.</summary>
    public static async Task RevokeAsync(AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (GetSessionId(principal) is not { } sessionId) return;
        await RevokeTrackedAsync(db, sessionId, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task RevokeTrackedAsync(AppDbContext db, Guid sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null && session.RevokedAt is null) session.RevokedAt = now;

        // O despachante já ignora inscrições de sessão inválida; desativar aqui só deixa o estado explícito.
        var subs = await db.PushSubscriptions.Where(s => s.SessionId == sessionId && s.IsActive).ToListAsync(ct);
        foreach (var s in subs)
        {
            s.IsActive = false;
            s.DeactivatedAt = now;
        }
    }
}
