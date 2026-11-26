namespace FutebolAtemporal.Domain.Enums;

public enum GroupRole
{
    Player = 0,
    Admin = 1
}

public enum MembershipStatus
{
    Active = 0,
    Inactive = 1,
    Blocked = 2
}

public enum MatchStatus
{
    Draft = 0,
    RegistrationOpen = 1,
    RegistrationClosed = 2,
    Drawn = 3,
    InProgress = 4,
    AwaitingClose = 5,
    Completed = 6,
    Cancelled = 7
}

public enum RegistrationStatus
{
    Confirmed = 0,
    Waitlisted = 1,
    Withdrawn = 2,
    Removed = 3,
    Absent = 4,
    Participated = 5
}

public enum PriorityClass
{
    Casual = 0,
    Monthly = 1
}

public enum MonthlyMembershipStatus
{
    Active = 0,
    Inactive = 1,
    Suspended = 2
}

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Overdue = 2,
    Exempt = 3,
    Cancelled = 4
}

public enum TeamDrawStatus
{
    Draft = 0,
    Confirmed = 1,
    Superseded = 2
}

/// <summary>Função do jogador no time sorteado. Sorteios antigos (A/B) ficam como Line.</summary>
public enum TeamRole
{
    Line = 0,
    Goalkeeper = 1
}

public enum AwardType
{
    /// <summary>Melhor Jogador da partida (antigo "craque").</summary>
    ManOfTheMatch = 0,
    /// <summary>Melhor Goleiro da partida: exige ter atuado no gol.</summary>
    BestGoalkeeper = 1
}

public enum PaymentSource
{
    None = 0,
    Manual = 1,
    Pix = 2
}

public enum PixChargeKind
{
    Membership = 0,
    MatchDay = 1
}

public enum PixChargeStatus
{
    Pending = 0,
    Approved = 1,
    Cancelled = 2,
    Expired = 3
}
public enum RoundGameStatus
{
    InProgress = 0,
    Finished = 1
}
