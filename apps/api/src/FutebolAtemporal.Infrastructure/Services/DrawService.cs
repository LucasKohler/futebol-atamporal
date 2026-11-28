using FutebolAtemporal.Application.Notifications;
using System.Text.Json;
using FutebolAtemporal.Application.Draws;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class DrawService : IDrawService
{
    private const int MaxSeedLength = 80;
    private readonly AppDbContext _db;
    private readonly INotificationTriggerService? _notifications;

    public DrawService(AppDbContext db, INotificationTriggerService? notifications = null)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<DrawDto> CreateDrawAsync(Guid userId, Guid matchId, string? seed, CancellationToken ct = default)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        DrawRosterGuard.EnsureEditable(match);
        if (seed is { Length: > MaxSeedLength })
            throw new DomainException($"A semente deve ter no máximo {MaxSeedLength} caracteres.");

        var confirmed = await _db.MatchRegistrations
            .Where(r => r.MatchId == matchId && r.Status == RegistrationStatus.Confirmed)
            .Select(r => r.PlayerProfileId)
            .ToListAsync(ct);

        var profiles = await _db.PlayerProfiles.Where(p => confirmed.Contains(p.Id)).ToListAsync(ct);
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, confirmed, ct);
        var candidates = profiles.Select(p => new DrawCandidate(
            p.Id, names.GetValueOrDefault(p.Id, "Jogador"), p.SkillRating, p.CanPlayGoalkeeper, p.GoalkeeperRating)).ToList();

        var result = ThreeColorDraw.Draw(candidates, seed);

        var previous = await _db.TeamDraws.Where(d => d.MatchId == matchId && d.Status != TeamDrawStatus.Superseded).ToListAsync(ct);
        foreach (var p in previous)
            p.Status = TeamDrawStatus.Superseded;

        var draw = new TeamDraw
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Seed = result.Seed,
            Status = TeamDrawStatus.Draft,
            TeamCount = result.Teams.Count,
            AlgorithmVersion = result.AlgorithmVersion,
            CreatedByUserId = userId
        };

        foreach (var team in result.Teams)
        {
            var mt = new MatchTeam { Id = Guid.NewGuid(), TeamDrawId = draw.Id, Name = team.Color, SortOrder = team.SortOrder };
            foreach (var member in team.Members)
            {
                mt.Members.Add(new MatchTeamMember
                {
                    Id = Guid.NewGuid(),
                    MatchTeamId = mt.Id,
                    PlayerProfileId = member.PlayerProfileId,
                    Role = member.Role,
                    SkillRatingSnapshot = member.LineRating,
                    CanPlayGoalkeeperSnapshot = member.CanPlayGoalkeeper,
                    GoalkeeperRatingSnapshot = member.GoalkeeperRating
                });
            }
            draw.Teams.Add(mt);
        }

        _db.TeamDraws.Add(draw);
        // A prévia nova ainda não vale: a lista fica encerrada aguardando confirmação.
        if (match.Status is MatchStatus.RegistrationOpen or MatchStatus.Drawn)
            match.Status = MatchStatus.RegistrationClosed;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++;
        Audit(match, userId, "draw.created", draw.Id, new
        {
            seed = result.Seed,
            version = result.AlgorithmVersion,
            candidates = result.DistinctCandidates,
            finalists = result.FinalistCandidates
        });

        await _db.SaveChangesAsync(ct);
        return (await MapAsync(userId, match, draw.Id, ct))!;
    }

    public async Task<DrawDto> ConfirmDrawAsync(Guid userId, Guid matchId, Guid drawId, CancellationToken ct = default)
    {
        var (match, draw) = await LoadForAdminAsync(userId, matchId, drawId, ct);
        if (draw.Status == TeamDrawStatus.Confirmed)
            return (await MapAsync(userId, match, draw.Id, ct))!; // confirmação repetida: idempotente

        DrawRosterGuard.EnsureEditable(match);
        var current = await MapAsync(userId, match, draw.Id, ct)
            ?? throw new DomainException("Sorteio não encontrado.");
        if (current.RosterChanged)
            throw new DomainException("A lista de confirmados mudou depois do sorteio. Refaça o sorteio ou ajuste antes de confirmar.");
        if (current.Blockers.Count > 0)
            throw new DomainException(string.Join(" ", current.Blockers));

        draw.Status = TeamDrawStatus.Confirmed;
        draw.ConfirmedAt = DateTimeOffset.UtcNow;
        if (match.Status == MatchStatus.RegistrationOpen)
        {
            MatchTransitions.EnsureTransition(match.Status, MatchStatus.RegistrationClosed);
            match.Status = MatchStatus.RegistrationClosed;
        }
        if (match.Status == MatchStatus.RegistrationClosed)
        {
            MatchTransitions.EnsureTransition(match.Status, MatchStatus.Drawn);
            match.Status = MatchStatus.Drawn;
        }
        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++; // duas confirmações simultâneas: a segunda falha por concorrência (409)
        Audit(match, userId, "draw.confirmed", draw.Id, new { seed = draw.Seed });

        if (_notifications is not null)
        {
            var assignments = draw.Teams
                .SelectMany(t => t.Members.Select(m => (m.PlayerProfileId, t.Name)))
                .ToList();
            await _notifications.EnqueueDrawPublishedAsync(match.GroupId, match.Id, draw.Id, draw.ConfirmedAt!.Value, assignments, ct);
        }

        await _db.SaveChangesAsync(ct);
        _notifications?.SignalDispatcher();
        return (await MapAsync(userId, match, drawId, ct))!;
    }

    public async Task<DrawDto?> GetCurrentAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        var match = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return null;
        var (membership, _) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        var isAdmin = membership.Role == GroupRole.Admin;

        var draws = await _db.TeamDraws.AsNoTracking()
            .Where(d => d.MatchId == matchId && d.Status != TeamDrawStatus.Superseded)
            .ToListAsync(ct);
        // Jogador vê só a escalação confirmada; a prévia é do admin.
        var draw = draws
            .Where(d => isAdmin || d.Status == TeamDrawStatus.Confirmed)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();
        if (draw is null) return null;
        return await MapAsync(userId, match, draw.Id, ct);
    }

    /// <summary>
    /// Troca dois jogadores de lugar (time e função). Quem passa a ser goleiro precisa estar apto.
    /// A troca invalida a confirmação anterior e fica registrada como desvio na auditoria.
    /// </summary>
    public async Task<DrawDto> SwapPlayersAsync(Guid userId, Guid matchId, Guid drawId, Guid playerA, Guid playerB, CancellationToken ct = default)
    {
        var (match, draw) = await LoadForAdminAsync(userId, matchId, drawId, ct);
        DrawRosterGuard.EnsureEditable(match);
        if (draw.Status == TeamDrawStatus.Superseded)
            throw new DomainException("Este sorteio foi substituído por outro.");

        var members = draw.Teams.SelectMany(t => t.Members).ToList();
        var ma = members.FirstOrDefault(m => m.PlayerProfileId == playerA)
            ?? throw new DomainException("O primeiro jogador não está no sorteio.");
        var mb = members.FirstOrDefault(m => m.PlayerProfileId == playerB)
            ?? throw new DomainException("O segundo jogador não está no sorteio.");
        if (ma.MatchTeamId == mb.MatchTeamId && ma.Role == mb.Role)
            throw new DomainException("Os dois jogadores já estão no mesmo time e na mesma função.");

        var profiles = await _db.PlayerProfiles.Where(p => p.Id == playerA || p.Id == playerB).ToDictionaryAsync(p => p.Id, ct);
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, [playerA, playerB], ct);
        EnsureCanTakeRole(mb.Role, ma, profiles[playerA], names.GetValueOrDefault(playerA, "Jogador"));
        EnsureCanTakeRole(ma.Role, mb, profiles[playerB], names.GetValueOrDefault(playerB, "Jogador"));

        var from = new { team = ma.MatchTeamId, role = ma.Role };
        (ma.MatchTeamId, mb.MatchTeamId) = (mb.MatchTeamId, ma.MatchTeamId);
        (ma.Role, mb.Role) = (mb.Role, ma.Role);

        var wasConfirmed = draw.Status == TeamDrawStatus.Confirmed;
        draw.Status = TeamDrawStatus.Draft;
        draw.ConfirmedAt = null;
        if (match.Status == MatchStatus.Drawn)
            match.Status = MatchStatus.RegistrationClosed;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        match.Version++;
        Audit(match, userId, "draw.swap", draw.Id, new
        {
            playerA, playerB, fromTeam = from.team, fromRole = from.role.ToString(), invalidatedConfirmation = wasConfirmed
        });

        await _db.SaveChangesAsync(ct);
        return (await MapAsync(userId, match, draw.Id, ct))!;
    }

    private static void EnsureCanTakeRole(TeamRole newRole, MatchTeamMember member, PlayerProfile profile, string name)
    {
        if (newRole != TeamRole.Goalkeeper || member.Role == TeamRole.Goalkeeper) return;
        if (!profile.CanPlayGoalkeeper)
            throw new DomainException($"{name} não está marcado como apto para o gol.");
        // Atualiza o retrato de goleiro de quem assume o gol na troca.
        member.CanPlayGoalkeeperSnapshot = true;
        member.GoalkeeperRatingSnapshot = profile.GoalkeeperRating;
    }

    private async Task<(Match Match, TeamDraw Draw)> LoadForAdminAsync(Guid userId, Guid matchId, Guid drawId, CancellationToken ct)
    {
        var draw = await _db.TeamDraws.Include(d => d.Teams).ThenInclude(t => t.Members)
            .FirstOrDefaultAsync(d => d.Id == drawId && d.MatchId == matchId, ct)
            ?? throw new DomainException("Sorteio não encontrado nesta partida.");
        var match = await _db.Matches.FirstAsync(m => m.Id == matchId, ct);
        await AccessHelper.RequireAdminForMatchAsync(_db, userId, match, ct);
        return (match, draw);
    }

    private void Audit(Match match, Guid userId, string action, Guid drawId, object payload)
        => _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = match.GroupId,
            ActorUserId = userId,
            Action = action,
            EntityType = nameof(TeamDraw),
            EntityId = drawId,
            PayloadJson = JsonSerializer.Serialize(payload)
        });

    private async Task<DrawDto?> MapAsync(Guid userId, Match match, Guid drawId, CancellationToken ct)
    {
        var isAdmin = await _db.GroupMemberships.AsNoTracking().AnyAsync(
            m => m.UserId == userId && m.GroupId == match.GroupId && m.Status == MembershipStatus.Active && m.Role == GroupRole.Admin, ct);

        // Lê o estado salvo + alterações pendentes no contexto (após troca, antes de recarregar).
        var draw = await _db.TeamDraws.Include(d => d.Teams).ThenInclude(t => t.Members)
            .FirstOrDefaultAsync(d => d.Id == drawId, ct);
        if (draw is null) return null;

        var memberIds = draw.Teams.SelectMany(t => t.Members).Select(m => m.PlayerProfileId).ToList();
        var confirmedIds = await _db.MatchRegistrations.AsNoTracking()
            .Where(r => r.MatchId == match.Id && r.Status == RegistrationStatus.Confirmed)
            .Select(r => r.PlayerProfileId).ToListAsync(ct);
        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, memberIds.Union(confirmedIds), ct);
        string Name(Guid id) => names.GetValueOrDefault(id, "Jogador");

        var legacy = draw.AlgorithmVersion is null;
        var colorTeams = draw.Teams.OrderBy(t => t.SortOrder).Select(t => new ColorTeam(
            t.Name, t.SortOrder,
            t.Members
                .OrderByDescending(m => m.Role).ThenByDescending(m => m.SkillRatingSnapshot).ThenBy(m => Name(m.PlayerProfileId), StringComparer.OrdinalIgnoreCase)
                .Select(m => new ColorTeamMember(m.PlayerProfileId, Name(m.PlayerProfileId), m.Role, m.SkillRatingSnapshot,
                    m.CanPlayGoalkeeperSnapshot, m.GoalkeeperRatingSnapshot)).ToList())).ToList();
        var balance = ThreeColorDraw.Evaluate(colorTeams);

        var (rosterChanged, changes) = DrawRosterGuard.Compare(memberIds, confirmedIds, Name);
        var warnings = legacy ? ["Sorteio antigo (Time A/B): cores e goleiros indisponíveis."] : balance.Warnings.ToList();
        var blockers = legacy ? [] : balance.Blockers.ToList();
        var locked = !DrawRosterGuard.IsEditable(match.Status);
        var canConfirm = draw.Status == TeamDrawStatus.Draft && !rosterChanged && blockers.Count == 0 && !locked;

        // Evaluate preserva a ordem por SortOrder: time i, entidade i e métrica i são o mesmo time.
        var entities = draw.Teams.OrderBy(t => t.SortOrder).ToList();
        var teams = colorTeams.Select((ct2, i) =>
        {
            var entity = entities[i];
            var m = balance.Teams[i];
            var color = ThreeColorDraw.Colors.Contains(entity.Name) ? entity.Name : null;
            return new DrawTeamDto(
                entity.Id, entity.Name, color, entity.SortOrder, m.LineCount,
                isAdmin ? m.LineSum : null,
                isAdmin ? m.LineAverage : null,
                legacy ? null : m.Goalkeeper?.DisplayName,
                isAdmin && !legacy ? m.Goalkeeper?.GoalkeeperRating : null,
                ct2.Members.Select(x => new DrawMemberDto(
                    x.PlayerProfileId, x.DisplayName, x.Role.ToString(),
                    isAdmin ? x.LineRating : null,
                    isAdmin ? x.GoalkeeperRating : null)).ToList(),
                isAdmin ? m.LineSum : null);
        }).ToList();

        return new DrawDto(
            draw.Id, draw.MatchId, draw.Seed, draw.Status.ToString(), draw.TeamCount, draw.AlgorithmVersion, legacy,
            draw.CreatedAt, draw.ConfirmedAt,
            isAdmin ? balance.LineSumSpread : null,
            isAdmin ? balance.LineAverageSpread : null,
            isAdmin ? balance.LineSumSpread : null,
            balance.LineCountSpread,
            balance.TotalCountSpread,
            legacy ? 0 : balance.GoalkeepersAssigned,
            isAdmin && !legacy ? balance.GoalkeeperSpread : null,
            isAdmin ? warnings : [],
            isAdmin ? blockers : [],
            isAdmin && rosterChanged,
            isAdmin ? changes : [],
            isAdmin && canConfirm,
            locked,
            teams);
    }
}

/// <summary>Regras que protegem o elenco sorteado quando a lista muda (PR-3/PO-3).</summary>
public static class DrawRosterGuard
{
    public static bool IsEditable(MatchStatus status)
        => status is MatchStatus.RegistrationOpen or MatchStatus.RegistrationClosed or MatchStatus.Drawn;

    public static void EnsureEditable(Match match)
    {
        if (!IsEditable(match.Status))
            throw new DomainException("Partida em andamento ou encerrada: o elenco sorteado está preservado e não pode ser alterado.");
    }

    public static (bool Changed, IReadOnlyList<string> Changes) Compare(
        IEnumerable<Guid> drawMembers, IEnumerable<Guid> confirmed, Func<Guid, string> name)
    {
        var inDraw = drawMembers.ToHashSet();
        var nowConfirmed = confirmed.ToHashSet();
        var left = inDraw.Except(nowConfirmed).Select(name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var joined = nowConfirmed.Except(inDraw).Select(name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var changes = new List<string>();
        if (left.Count > 0) changes.Add($"Saíram da lista: {string.Join(", ", left)}.");
        if (joined.Count > 0) changes.Add($"Entraram na lista: {string.Join(", ", joined)}.");
        return (changes.Count > 0, changes);
    }

    /// <summary>
    /// Chamado quando a lista de confirmados muda (desistência, remoção, promoção, limite).
    /// Se o elenco do sorteio ativo não bate mais com os confirmados, a confirmação cai e a partida volta a
    /// "inscrições encerradas" até o admin refazer ou revisar o sorteio.
    /// </summary>
    public static async Task InvalidateIfRosterChangedAsync(AppDbContext db, Match match, IEnumerable<Guid> confirmedIds, CancellationToken ct)
    {
        // Defesa em profundidade: depois do início (ou encerrada) a escalação confirmada nunca é rebaixada.
        if (!IsEditable(match.Status)) return;

        // Ordenação em memória: SQLite não ordena DateTimeOffset no banco.
        var draw = (await db.TeamDraws.Include(d => d.Teams).ThenInclude(t => t.Members)
                .Where(d => d.MatchId == match.Id && d.Status != TeamDrawStatus.Superseded)
                .ToListAsync(ct))
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();
        if (draw is null) return;

        var (changed, _) = Compare(draw.Teams.SelectMany(t => t.Members).Select(m => m.PlayerProfileId), confirmedIds, _ => "");
        if (!changed) return;

        if (draw.Status == TeamDrawStatus.Confirmed)
        {
            draw.Status = TeamDrawStatus.Draft;
            draw.ConfirmedAt = null;
        }
        if (match.Status == MatchStatus.Drawn)
            match.Status = MatchStatus.RegistrationClosed;
    }
}
