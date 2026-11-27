using System.Security.Claims;
using FutebolAtemporal.Application.Abstractions;
using FutebolAtemporal.Application.Auth;
using FutebolAtemporal.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutebolAtemporal.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var (ok, error) = await _auth.RegisterAsync(request, ct);
        if (!ok) return BadRequest(new { title = "Registro falhou", detail = error });
        var me = await _auth.GetCurrentUserAsync(ct);
        return Ok(me);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var (ok, error, code) = await _auth.LoginAsync(request, ct);
        if (!ok) return Unauthorized(new { title = "Login falhou", detail = error, code });
        var me = await _auth.GetCurrentUserAsync(ct);
        return Ok(me);
    }

    /// <summary>Cria a senha do primeiro admin com o código de configuração; depois fica desativado.</summary>
    [HttpPost("admin-setup")]
    [AllowAnonymous]
    public async Task<IActionResult> AdminSetup([FromBody] AdminSetupRequest request, CancellationToken ct)
    {
        var (ok, status, error, code) = await _auth.SetupFirstAdminAsync(request, ct);
        if (!ok) return StatusCode(status, new { title = "Configuração do admin falhou", detail = error, code });
        return Ok(await _auth.GetCurrentUserAsync(ct));
    }

    /// <summary>Recuperação do admin: código temporário do operador, uso único, só para o celular do admin.</summary>
    [HttpPost("admin-recovery")]
    [AllowAnonymous]
    public async Task<IActionResult> AdminRecovery([FromBody] AdminRecoveryRequest request, CancellationToken ct)
    {
        var (ok, status, error, code) = await _auth.RecoverAdminAsync(request, ct);
        if (!ok) return StatusCode(status, new { title = "Recuperação do admin falhou", detail = error, code });
        return Ok(await _auth.GetCurrentUserAsync(ct));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _auth.LogoutAsync(ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var me = await _auth.GetCurrentUserAsync(ct);
        if (me is null) return Unauthorized();
        return Ok(me);
    }

    [HttpPut("me")]
    [HttpPatch("me")]
    [Authorize]
    public IActionResult UpdateMe([FromBody] UpdateProfileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            throw new ForbiddenException("O nome cadastrado não pode ser alterado.");
        if (request.Nickname is not null)
            throw new ForbiddenException("Apenas administradores definem apelido.");
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/groups")]
public sealed class GroupsController : ControllerBase
{
    private readonly IGroupQueryService _groups;
    private readonly IAuthService _auth;

    public GroupsController(IGroupQueryService groups, IAuthService auth)
    {
        _groups = groups;
        _auth = auth;
    }

    /// <summary>Recuperação de senha: o admin do grupo define a nova senha do jogador.</summary>
    [HttpPut("{groupId:guid}/players/{playerProfileId:guid}/password")]
    public async Task<IActionResult> ResetPassword(Guid groupId, Guid playerProfileId, [FromBody] AdminResetPasswordRequest body, CancellationToken ct)
    {
        await _auth.AdminResetPasswordAsync(User.GetUserId(), groupId, playerProfileId, body, ct);
        return NoContent();
    }

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id)) return Unauthorized();
        var group = await _groups.GetCurrentGroupAsync(id, ct);
        if (group is null) return NotFound(new { title = "Grupo não encontrado" });
        return Ok(group);
    }

    [HttpGet("{groupId:guid}")]
    public async Task<IActionResult> GetById(Guid groupId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id)) return Unauthorized();
        var group = await _groups.GetByIdAsync(groupId, id, ct);
        if (group is null) return NotFound(new { title = "Grupo não encontrado" });
        return Ok(group);
    }

    [HttpGet("{groupId:guid}/players")]
    public async Task<IActionResult> Players(Guid groupId, CancellationToken ct)
        => Ok(await _groups.ListPlayersAsync(User.GetUserId(), groupId, ct));

    [HttpPut("{groupId:guid}/players/{playerProfileId:guid}/nickname")]
    public async Task<IActionResult> Nickname(Guid groupId, Guid playerProfileId, [FromBody] SetNicknameRequest body, CancellationToken ct)
        => Ok(await _groups.SetNicknameAsync(User.GetUserId(), groupId, playerProfileId, body.Nickname, ct));

    [HttpPut("{groupId:guid}/players/{playerProfileId:guid}/ratings")]
    public async Task<IActionResult> Ratings(Guid groupId, Guid playerProfileId, [FromBody] SetRatingsRequest body, CancellationToken ct)
        => Ok(await _groups.SetRatingsAsync(User.GetUserId(), groupId, playerProfileId, body, ct));
}
