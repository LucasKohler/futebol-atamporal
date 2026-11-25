using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Domain.Entities;

public class FootballGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "America/Sao_Paulo";
    public string SettingsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<GroupMembership> Memberships { get; set; } = new List<GroupMembership>();
    public ICollection<Match> Matches { get; set; } = new List<Match>();
}

public class GroupMembership
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FootballGroup Group { get; set; } = null!;
    public Guid UserId { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Player;
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PlayerProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? Phone { get; set; }
    public string? PhotoUrl { get; set; }
    /// <summary>Nota de linha (0–10). Mantém o nome histórico da coluna.</summary>
    public decimal SkillRating { get; set; } = 5.0m;
    /// <summary>Aptidão para jogar no gol; independente da nota de linha.</summary>
    public bool CanPlayGoalkeeper { get; set; }
    /// <summary>Nota de goleiro (0–10). Nulo = sem nota; zero é uma nota válida.</summary>
    public decimal? GoalkeeperRating { get; set; }
    public string? Bio { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Match
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FootballGroup Group { get; set; } = null!;
    public string? Title { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public int PlayerLimit { get; set; } = 14;
    public MatchStatus Status { get; set; } = MatchStatus.Draft;
    public string? Notes { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<MatchRegistration> Registrations { get; set; } = new List<MatchRegistration>();
}

public class MatchRegistration
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
    public RegistrationStatus Status { get; set; }
    public PriorityClass PriorityClass { get; set; } = PriorityClass.Casual;
    public int? WaitlistPosition { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class MonthlyMembership
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FootballGroup Group { get; set; } = null!;
    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
    public MonthlyMembershipStatus Status { get; set; } = MonthlyMembershipStatus.Active;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
}

public class MembershipBillingPeriod
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FootballGroup Group { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class MembershipPayment
{
    public Guid Id { get; set; }
    public Guid MonthlyMembershipId { get; set; }
    public MonthlyMembership MonthlyMembership { get; set; } = null!;
    public Guid BillingPeriodId { get; set; }
    public MembershipBillingPeriod BillingPeriod { get; set; } = null!;
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTimeOffset? PaidAt { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TeamDraw
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public string Seed { get; set; } = string.Empty;
    public TeamDrawStatus Status { get; set; } = TeamDrawStatus.Draft;
    public int TeamCount { get; set; } = 2;
    /// <summary>Versão do algoritmo (ex.: "3cores-v1"). Nulo = sorteio antigo A/B.</summary>
    public string? AlgorithmVersion { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public ICollection<MatchTeam> Teams { get; set; } = new List<MatchTeam>();
}

public class MatchTeam
{
    public Guid Id { get; set; }
    public Guid TeamDrawId { get; set; }
    public TeamDraw TeamDraw { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public ICollection<MatchTeamMember> Members { get; set; } = new List<MatchTeamMember>();
}

public class MatchTeamMember
{
    public Guid Id { get; set; }
    public Guid MatchTeamId { get; set; }
    public MatchTeam MatchTeam { get; set; } = null!;
    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
    /// <summary>Nota de linha no momento do sorteio.</summary>
    public decimal SkillRatingSnapshot { get; set; }
    /// <summary>Função na escalação. Sorteios antigos ficam como Line.</summary>
    public TeamRole Role { get; set; } = TeamRole.Line;
    public bool CanPlayGoalkeeperSnapshot { get; set; }
    public decimal? GoalkeeperRatingSnapshot { get; set; }
}

public class MatchResult
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public Guid? WinnerTeamId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset ClosedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid ClosedByUserId { get; set; }
}

/// <summary>
/// Revisão da súmula de uma rodada de três cores (PO-1/PO-2). Cada correção cria uma nova revisão
/// com motivo; só a revisão vigente (IsCurrent) entra em vitórias, artilharia e prêmios.
/// A súmula (confrontos, gols, presença e prêmios) fica em JSON para gravar a revisão inteira de forma atômica.
/// </summary>
public class MatchResultRevision
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int Revision { get; set; }
    public bool IsCurrent { get; set; }
    public string SheetJson { get; set; } = "{}";
    /// <summary>Obrigatório a partir da revisão 2 (correção).</summary>
    public string? Reason { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PlayerMatchRating
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
    public decimal Score { get; set; }
    public Guid RatedByUserId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PlayerAward
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
    public AwardType Type { get; set; } = AwardType.ManOfTheMatch;
    public Guid AwardedByUserId { get; set; }
}

public class AuditEntry
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
