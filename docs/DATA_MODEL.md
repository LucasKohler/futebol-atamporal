# Data Model — Futebol Atemporal

Modelo inicial alinhado ao PRD e às regras de domínio. Detalhes de colunas evoluem via migrations.

## Diagrama lógico

```
User ──< GroupMembership >── FootballGroup
User ──1 PlayerProfile
FootballGroup ──< Match
FootballGroup ──< Membership >── PlayerProfile
Match ──< MatchRegistration >── PlayerProfile
Match ──1 TeamDraw ──< MatchTeam ──< MatchTeamMember
Match ──1 MatchResult
Match ──< PlayerMatchRating
Match ──< PlayerAward
Membership ──< MembershipPayment >── MembershipBillingPeriod
FootballGroup ──< AuditEntry
```

## Entidades

### User (Identity)

- Id (Guid), UserName = celular normalizado, DisplayName (nome cadastrado), Nickname? (apelido admin), IsActive, CreatedAt
- Sem e-mail/senha no fluxo; Identity permanece para cookie/sessão

### PlayerProfile

- Id, UserId (unique), Phone?, PhotoUrl?, SkillRating (decimal 0–10, default 5.0), Bio?
- Estatísticas derivadas preferencialmente por query (não duplicar regras)

### FootballGroup

- Id, Name, Slug, TimeZoneId, Settings (JSON: capacity default, priority deadline, team count default)
- CreatedAt

### GroupMembership

- Id, GroupId, UserId, Role (`Player` | `Admin`), Status (`Active` | `Inactive` | `Blocked`)
- Unique (GroupId, UserId)

### Match

- Id, GroupId, Title?, StartsAt, Location, PlayerLimit, Status, Notes?
- RowVersion / xmin para concorrência
- Unique operacional: no máximo uma “próxima” partida aberta por grupo (regra de app, não necessariamente constraint)

### MatchRegistration

- Id, MatchId, PlayerProfileId, Status, PriorityClass (`Monthly` | `Casual`)
- WaitlistPosition (nullable), ConfirmedAt?, WithdrawnAt?
- Unique (MatchId, PlayerProfileId)

### Membership

- Id, GroupId, PlayerProfileId, Status (`Active` | `Inactive` | `Suspended`), StartedAt, EndedAt?
- Unique ativo (GroupId, PlayerProfileId) onde Status = Active

### MembershipBillingPeriod

- Id, GroupId, Year, Month, Label
- Unique (GroupId, Year, Month)

### MembershipPayment

- Id, MembershipId, BillingPeriodId, Amount, Status, PaidAt?, Notes?, RecordedByUserId
- Unique (MembershipId, BillingPeriodId) para lançamento vigente

### TeamDraw

- Id, MatchId, Seed (string/long), Status (`Draft` | `Confirmed`), CreatedByUserId, CreatedAt, ConfirmedAt?
- TeamCount

### MatchTeam

- Id, TeamDrawId, Name (A/B/…), SortOrder

### MatchTeamMember

- Id, MatchTeamId, PlayerProfileId, SkillRatingSnapshot
- Unique (TeamDrawId via team, PlayerProfileId) — um jogador por draw

### MatchResult

- Id, MatchId, TeamScores (JSON ou tabela), WinnerTeamId?, Notes?, ClosedAt, ClosedByUserId

### PlayerMatchRating

- Id, MatchId, PlayerProfileId, Score, RatedByUserId, Notes?, CreatedAt
- Unique (MatchId, PlayerProfileId)

### PlayerAward

- Id, MatchId, PlayerProfileId, Type (`ManOfTheMatch`), AwardedByUserId

### AuditEntry

- Id, GroupId, ActorUserId, Action, EntityType, EntityId, PayloadJson, CreatedAt

## Índices sugeridos

- `MatchRegistration (MatchId, Status, WaitlistPosition)`
- `Match (GroupId, StartsAt)`
- `MembershipPayment (BillingPeriodId, Status)`
- `PlayerMatchRating (PlayerProfileId, MatchId)`

## Soft delete

Evitar soft delete global. Preferir status explícitos (`Withdrawn`, `Cancelled`, `Inactive`).
