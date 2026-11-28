using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace FutebolAtemporal.Api.Controllers;

/// <summary>
/// Precondição de identidade nas operações iniciadas pelo cliente para uma conta específica.
/// O cookie é compartilhado entre abas e pode trocar de conta entre a consulta de sessão e a requisição;
/// por isso a própria requisição informa <c>expectedUserId</c> e a API o compara com o principal autenticado
/// antes de qualquer mutação. O id esperado nunca substitui o principal como dono dos dados.
/// </summary>
internal static class SessionPrecondition
{
    public const string Code = "session_changed";

    /// <summary>True só quando <paramref name="expectedUserId"/> é um UUID igual ao usuário autenticado.</summary>
    public static bool Matches(ClaimsPrincipal user, string? expectedUserId)
        => Guid.TryParse(expectedUserId, out var expected) && expected == user.GetUserId();

    /// <summary>409 genérico: não revela o id esperado nem o da sessão atual.</summary>
    public static ObjectResult SessionChanged(this ControllerBase controller)
    {
        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode: StatusCodes.Status409Conflict,
            title: "Sessão alterada",
            detail: "A sessão mudou antes de concluir a operação. Atualize a página e tente de novo.");
        problem.Extensions["code"] = Code;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }
}
