using System.Security.Claims;
using FutebolAtemporal.Application.Abstractions;
using FutebolAtemporal.Application.Auth;
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
        var (ok, error) = await _auth.LoginAsync(request, ct);
        if (!ok) return Unauthorized(new { title = "Login falhou", detail = error });
        var me = await _auth.GetCurrentUserAsync(ct);
        return Ok(me);
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
}

[ApiController]
[Authorize]
[Route("api/groups")]
public sealed class GroupsController : ControllerBase
{
    private readonly IGroupQueryService _groups;

    public GroupsController(IGroupQueryService groups) => _groups = groups;

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
}
