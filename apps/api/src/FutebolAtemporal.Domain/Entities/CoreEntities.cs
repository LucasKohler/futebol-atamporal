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
    public PaymentSource PaymentSource { get; set; } = PaymentSource.None;
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

/// <summary>Obrigação financeira de diária (avulso/diarista) vinculada a uma partida.</summary>
public class MatchFeePayment
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FootballGroup Group { get; set; } = null!;
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentSource PaymentSource { get; set; } = PaymentSource.None;
    public DateTimeOffset? PaidAt { get; set; }
    /// <summary>Fim do dia (fuso do grupo) do fechamento da rodada. Nulo = ainda não lançada no fechamento.</summary>
    public DateTimeOffset? DueAt { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Tentativa de cobrança Pix no Mercado Pago, separada da obrigação (mensalidade ou diária).</summary>
public class PixCharge
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FootballGroup Group { get; set; } = null!;
    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
    public PixChargeKind Kind { get; set; }
    public Guid? MembershipPaymentId { get; set; }
    public MembershipPayment? MembershipPayment { get; set; }
    public Guid? MatchFeePaymentId { get; set; }
    public MatchFeePayment? MatchFeePayment { get; set; }
    public Guid? MatchId { get; set; }
    public Match? Match { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string MpPaymentId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string QrCode { get; set; } = string.Empty;
    public string? QrCodeBase64 { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public PixChargeStatus Status { get; set; } = PixChargeStatus.Pending;
    public string? ProviderStatus { get; set; }
    public bool IsSimulated { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAt { get; set; }
}
/// <summary>
/// Rascunho ao vivo da pelada do dia (D2): guarda jogos, gols e defesas de pênalti em JSON v2
/// com controle de concorrência otimista via Version.
/// </summary>
public class MatchLiveSheet
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public string SheetJson { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
/// <summary>Inscrição Web Push (VAPID) vinculada ao usuário e aparelho (D2).</summary>
public class PushSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>
    /// Sessão de login que gravou a inscrição. Só recebe push enquanto essa sessão vale (não revogada,
    /// não expirada, mesmo carimbo de segurança). Nulo = sem sessão conhecida = não recebe.
    /// </summary>
    public Guid? SessionId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string VapidKeyId { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public string? DeviceLabel { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSuccessAt { get; set; }
    public int FailureCount { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
}

/// <summary>Campanha de notificação (automática, lembrete rápido ou comunicado admin).</summary>
public class NotificationCampaign
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FootballGroup Group { get; set; } = null!;
    public NotificationSource Source { get; set; }
    public NotificationCategory Category { get; set; }
    public NotificationAudience Audience { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = "/";
    public Guid? MatchId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public int RecipientCount { get; set; }
    public int PushDeviceCount { get; set; }
    public int PushSentCount { get; set; }
    public int PushFailedCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Notificação in-app por usuário e grupo, atuando também como outbox de push (D1/D3).</summary>
public class UserNotification
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public FootballGroup Group { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid CampaignId { get; set; }
    public NotificationCampaign Campaign { get; set; } = null!;
    public NotificationCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = "/";
    public Guid? MatchId { get; set; }
    public string? DedupeKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
    public NotificationPushState PushState { get; set; } = NotificationPushState.Pending;
    public int PushAttempts { get; set; }
    public DateTimeOffset? NextPushAt { get; set; }
    public int PushSentCount { get; set; }
    /// <summary>Reserva do envio por um despachante até este instante (evita dois despachantes no mesmo aviso).</summary>
    public DateTimeOffset? ClaimedUntil { get; set; }
    /// <summary>Token de concorrência da reserva: só um despachante consegue gravar a reserva.</summary>
    public int DispatchVersion { get; set; }
}

/// <summary>
/// Último lembrete rápido por grupo e categoria. Uma linha por par, com token de concorrência:
/// garante o intervalo de 30 min também entre várias instâncias da API.
/// </summary>
public class NotificationCooldown
{
    public Guid GroupId { get; set; }
    public NotificationCategory Category { get; set; }
    public DateTimeOffset LastSentAt { get; set; }
    public int Version { get; set; }
}

/// <summary>Entrega confirmada de um aviso a uma inscrição: novas tentativas pulam aparelhos já atendidos.</summary>
public class NotificationDelivery
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public UserNotification Notification { get; set; } = null!;
    public Guid SubscriptionId { get; set; }
    public PushSubscription Subscription { get; set; } = null!;
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
