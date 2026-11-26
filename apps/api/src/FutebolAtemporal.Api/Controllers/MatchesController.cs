using System.Security.Claims;
using FutebolAtemporal.Application.Draws;
using FutebolAtemporal.Application.Matches;
using FutebolAtemporal.Application.Memberships;
using FutebolAtemporal.Application.Results;
using FutebolAtemporal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutebolAtemporal.Api.Controllers;

internal static class UserExt
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var guid))
            throw new UnauthorizedAccessException();
        return guid;
    }
}

[ApiController]
[Authorize]
[Route("api/matches")]
public sealed class MatchesController : ControllerBase
{
    private readonly IMatchService _matches;
    private readonly IRegistrationService _registrations;

    public MatchesController(IMatchService matches, IRegistrationService registrations)
    {
        _matches = matches;
        _registrations = registrations;
    }

    [HttpPost]
    public async Task<ActionResult<MatchDto>> Create([FromBody] CreateMatchRequest request, CancellationToken ct)
        => Ok(await _matches.CreateAsync(User.GetUserId(), request, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MatchDto>>> List([FromQuery] Guid groupId, CancellationToken ct)
        => Ok(await _matches.ListAsync(User.GetUserId(), groupId, ct));

    [HttpGet("upcoming")]
    public async Task<ActionResult<MatchDetailDto>> Upcoming([FromQuery] Guid groupId, CancellationToken ct)
    {
        var detail = await _matches.GetUpcomingAsync(User.GetUserId(), groupId, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("{matchId:guid}")]
    public async Task<ActionResult<MatchDetailDto>> Get(Guid matchId, CancellationToken ct)
    {
        var detail = await _matches.GetAsync(User.GetUserId(), matchId, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPut("{matchId:guid}")]
    public async Task<ActionResult<MatchDto>> Update(Guid matchId, [FromBody] UpdateMatchRequest request, CancellationToken ct)
        => Ok(await _matches.UpdateAsync(User.GetUserId(), matchId, request, ct));

    [HttpPost("{matchId:guid}/transition")]
    public async Task<ActionResult<MatchDto>> Transition(Guid matchId, [FromBody] TransitionRequest body, CancellationToken ct)
        => Ok(await _matches.TransitionAsync(User.GetUserId(), matchId, body.Status, ct));

    [HttpPost("{matchId:guid}/join")]
    public async Task<ActionResult<MatchDetailDto>> Join(Guid matchId, CancellationToken ct)
        => Ok(await _registrations.JoinAsync(User.GetUserId(), matchId, ct));

    [HttpPost("{matchId:guid}/leave")]
    public async Task<ActionResult<MatchDetailDto>> Leave(Guid matchId, CancellationToken ct)
        => Ok(await _registrations.LeaveAsync(User.GetUserId(), matchId, ct));

    [HttpPost("{matchId:guid}/registrations/{playerProfileId:guid}/remove")]
    public async Task<ActionResult<MatchDetailDto>> Remove(Guid matchId, Guid playerProfileId, CancellationToken ct)
        => Ok(await _registrations.AdminRemoveAsync(User.GetUserId(), matchId, playerProfileId, ct));
}

public sealed record TransitionRequest(MatchStatus Status);

[ApiController]
[Authorize]
[Route("api/groups/{groupId:guid}/memberships")]
public sealed class MembershipsController : ControllerBase
{
    private readonly IMembershipService _svc;
    public MembershipsController(IMembershipService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult> List(Guid groupId, CancellationToken ct)
        => Ok(await _svc.ListMembersAsync(User.GetUserId(), groupId, ct));

    [HttpPost]
    public async Task<ActionResult> Add(Guid groupId, [FromBody] AddMemberRequest body, CancellationToken ct)
        => Ok(await _svc.AddMemberAsync(User.GetUserId(), groupId, body.PlayerProfileId, ct));

    [HttpPost("{membershipId:guid}/status")]
    public async Task<ActionResult> Status(Guid groupId, Guid membershipId, [FromBody] StatusRequest body, CancellationToken ct)
        => Ok(await _svc.SetStatusAsync(User.GetUserId(), groupId, membershipId, body.Status, ct));

    [HttpPost("periods")]
    public async Task<ActionResult> EnsurePeriod(Guid groupId, [FromBody] PeriodRequest body, CancellationToken ct)
        => Ok(await _svc.EnsurePeriodAsync(User.GetUserId(), groupId, body.Year, body.Month, ct));

    [HttpGet("payments")]
    public async Task<ActionResult> Payments(Guid groupId, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] PaymentStatus? status, CancellationToken ct)
        => Ok(await _svc.ListPaymentsAsync(User.GetUserId(), groupId, year, month, status, ct));

    [HttpPut("{membershipId:guid}/payments/{periodId:guid}")]
    public async Task<ActionResult> UpsertPayment(Guid groupId, Guid membershipId, Guid periodId, [FromBody] UpsertPaymentRequest body, CancellationToken ct)
        => Ok(await _svc.UpsertPaymentAsync(User.GetUserId(), groupId, membershipId, periodId, body, ct));
}

public sealed record AddMemberRequest(Guid PlayerProfileId);
public sealed record StatusRequest(MonthlyMembershipStatus Status);
public sealed record PeriodRequest(int Year, int Month);

[ApiController]
[Authorize]
[Route("api/matches/{matchId:guid}/draw")]
public sealed class DrawController : ControllerBase
{
    private readonly IDrawService _draws;
    public DrawController(IDrawService draws) => _draws = draws;

    [HttpGet]
    public async Task<ActionResult> Get(Guid matchId, CancellationToken ct)
    {
        var draw = await _draws.GetCurrentAsync(User.GetUserId(), matchId, ct);
        return draw is null ? NotFound() : Ok(draw);
    }

    [HttpPost]
    public async Task<ActionResult> Create(Guid matchId, [FromBody] CreateDrawRequest? body, CancellationToken ct)
        => Ok(await _draws.CreateDrawAsync(User.GetUserId(), matchId, body?.Seed, ct));

    [HttpPost("{drawId:guid}/confirm")]
    public async Task<ActionResult> Confirm(Guid matchId, Guid drawId, CancellationToken ct)
        => Ok(await _draws.ConfirmDrawAsync(User.GetUserId(), matchId, drawId, ct));

    [HttpPost("{drawId:guid}/swap")]
    public async Task<ActionResult> Swap(Guid matchId, Guid drawId, [FromBody] SwapRequest body, CancellationToken ct)
        => Ok(await _draws.SwapPlayersAsync(User.GetUserId(), matchId, drawId, body.PlayerA, body.PlayerB, ct));
}

public sealed record CreateDrawRequest(string? Seed);
public sealed record SwapRequest(Guid PlayerA, Guid PlayerB);

[ApiController]
[Authorize]
[Route("api/matches/{matchId:guid}/result")]
public sealed class ResultsController : ControllerBase
{
    private readonly IResultService _results;
    private readonly IRoundResultService _rounds;

    public ResultsController(IResultService results, IRoundResultService rounds)
    {
        _results = results;
        _rounds = rounds;
    }

    [HttpGet]
    public async Task<ActionResult> Get(Guid matchId, CancellationToken ct)
    {
        var r = await _results.GetAsync(User.GetUserId(), matchId, ct);
        return r is null ? NotFound() : Ok(r);
    }

    /// <summary>Fechamento antigo (placar A/B), mantido para sorteios antigos.</summary>
    [HttpPost("close")]
    public async Task<ActionResult> Close(Guid matchId, [FromBody] CloseMatchRequest body, CancellationToken ct)
        => Ok(await _results.CloseAsync(User.GetUserId(), matchId, body, ct));

    /// <summary>Súmula de três cores: vigente ou revisão anterior (?revision=N, só admin). Legado A/B vem com isLegacy.</summary>
    [HttpGet("round")]
    public async Task<ActionResult> GetRound(Guid matchId, [FromQuery] int? revision, CancellationToken ct)
    {
        var r = await _rounds.GetRoundAsync(User.GetUserId(), matchId, revision, ct);
        return r is null ? NotFound() : Ok(r);
    }

    [HttpPost("round")]
    public async Task<ActionResult> CloseRound(Guid matchId, [FromBody] RoundSheetRequest body, CancellationToken ct)
        => Ok(await _rounds.CloseRoundAsync(User.GetUserId(), matchId, body, ct));

    /// <summary>Correção auditada: exige motivo e cria nova revisão (a anterior continua consultável).</summary>
    [HttpPut("round")]
    public async Task<ActionResult> CorrectRound(Guid matchId, [FromBody] RoundSheetRequest body, CancellationToken ct)
        => Ok(await _rounds.CorrectRoundAsync(User.GetUserId(), matchId, body, ct));
}

[ApiController]
[Authorize]
[Route("api/groups/{groupId:guid}/season")]
public sealed class SeasonController : ControllerBase
{
    private readonly IRoundResultService _rounds;
    public SeasonController(IRoundResultService rounds) => _rounds = rounds;

    /// <summary>Artilharia do mês/ano (data local do jogo), vitórias por cor, jogadores e rodadas.</summary>
    [HttpGet]
    public async Task<ActionResult> Get(Guid groupId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
        => Ok(await _rounds.GetSeasonAsync(User.GetUserId(), groupId, year, month, ct));
}

[ApiController]
[Authorize]
[Route("api/groups/{groupId:guid}/rankings")]
public sealed class RankingsController : ControllerBase
{
    private readonly IStatsService _stats;
    public RankingsController(IStatsService stats) => _stats = stats;

    [HttpGet]
    public async Task<ActionResult> Get(Guid groupId, [FromQuery] string sortBy = "wins", CancellationToken ct = default)
        => Ok(await _stats.GetRankingsAsync(User.GetUserId(), groupId, sortBy, ct));
}

[ApiController]
[Authorize]
[Route("api/players")]
public sealed class PlayersController : ControllerBase
{
    private readonly IStatsService _stats;
    public PlayersController(IStatsService stats) => _stats = stats;

    [HttpGet("{playerProfileId:guid}/history")]
    public async Task<ActionResult> History(Guid playerProfileId, CancellationToken ct)
    {
        var h = await _stats.GetPlayerHistoryAsync(User.GetUserId(), playerProfileId, ct);
        return h is null ? NotFound() : Ok(h);
    }
}
[ApiController]
[Authorize]
[Route("api/matches/{matchId:guid}/live")]
public sealed class LiveMatchController : ControllerBase
{
    private readonly IRoundResultService _rounds;
    public LiveMatchController(IRoundResultService rounds) => _rounds = rounds;

    [HttpGet]
    public async Task<ActionResult<LiveDayDto>> Get(Guid matchId, CancellationToken ct)
    {
        var live = await _rounds.GetLiveAsync(User.GetUserId(), matchId, ct);
        return live is null ? NotFound() : Ok(live);
    }

    [HttpPost("games")]
    public async Task<ActionResult<LiveDayDto>> StartOrInsertGame(Guid matchId, [FromBody] StartLiveGameRequest body, CancellationToken ct)
    {
        var live = await _rounds.StartOrInsertLiveGameAsync(User.GetUserId(), matchId, body, ct);
        return StatusCode(StatusCodes.Status201Created, live);
    }

    [HttpPost("games/{gameId:guid}/goals")]
    public async Task<ActionResult<LiveDayDto>> AddGoal(Guid matchId, Guid gameId, [FromBody] AddLiveGoalRequest body, CancellationToken ct)
        => Ok(await _rounds.AddLiveGoalAsync(User.GetUserId(), matchId, gameId, body, ct));

    [HttpDelete("games/{gameId:guid}/goals/{goalId:guid}")]
    public async Task<ActionResult<LiveDayDto>> RemoveGoal(Guid matchId, Guid gameId, Guid goalId, [FromQuery] int expectedVersion, CancellationToken ct)
        => Ok(await _rounds.RemoveLiveGoalAsync(User.GetUserId(), matchId, gameId, goalId, expectedVersion, ct));

    [HttpPost("games/{gameId:guid}/finish")]
    public async Task<ActionResult<LiveDayDto>> FinishGame(Guid matchId, Guid gameId, [FromBody] FinishLiveGameRequest body, CancellationToken ct)
        => Ok(await _rounds.FinishLiveGameAsync(User.GetUserId(), matchId, gameId, body, ct));

    [HttpPut("games/{gameId:guid}")]
    public async Task<ActionResult<LiveDayDto>> UpdateGame(Guid matchId, Guid gameId, [FromBody] UpdateLiveGameRequest body, CancellationToken ct)
        => Ok(await _rounds.UpdateLiveGameAsync(User.GetUserId(), matchId, gameId, body, ct));

    [HttpDelete("games/{gameId:guid}")]
    public async Task<ActionResult<LiveDayDto>> DeleteGame(Guid matchId, Guid gameId, [FromQuery] int expectedVersion, CancellationToken ct)
        => Ok(await _rounds.DeleteLiveGameAsync(User.GetUserId(), matchId, gameId, expectedVersion, ct));

    [HttpPut("presence")]
    public async Task<ActionResult<LiveDayDto>> UpdatePresence(Guid matchId, [FromBody] UpdateLivePresenceRequest body, CancellationToken ct)
        => Ok(await _rounds.UpdateLivePresenceAsync(User.GetUserId(), matchId, body, ct));
}
