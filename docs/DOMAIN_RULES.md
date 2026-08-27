# Domain Rules — Futebol Atemporal

Regras de negócio centralizadas. Hipóteses adotadas no autopilot estão marcadas com **[H]** e são configuráveis quando fizer sentido.

## 1. Grupo

- Toda operação de negócio ocorre no contexto de um `FootballGroup`.
- Papéis: `Player`, `Admin` (via `GroupMembership`).
- **[H]** Um usuário pode pertencer a múltiplos grupos; a UI inicial assume o grupo padrão do seed.

## 1.1 Identidade

- Identificador único: celular BR normalizado (só dígitos; `55` de país é removido).
- Cadastro: celular + nome. O jogador não altera o nome depois.
- Login: só o celular, sem senha.
- Apelido: só admin. Exibição = apelido se preenchido, senão nome cadastrado.

## 2. Partida — ciclo de vida

| Status | Significado |
|--------|-------------|
| `Draft` | Rascunho; não visível para inscrição |
| `RegistrationOpen` | Aceita entradas na lista/fila |
| `RegistrationClosed` | Sem novas inscrições; lista congelável para sorteio |
| `Drawn` | Times sorteados/confirmados |
| `InProgress` | Partida em andamento |
| `AwaitingClose` | Aguardando registro pós-jogo |
| `Completed` | Fechada; estatísticas aplicadas |
| `Cancelled` | Cancelada; inscrições inválidas |

Transições válidas são validadas no domínio (ex.: não sortear sem lista fechada ou aberta conforme política do grupo).

## 3. Inscrição

| Status | Significado |
|--------|-------------|
| `Confirmed` | Ocupa vaga |
| `Waitlisted` | Na fila; `WaitlistPosition` 1-based |
| `Withdrawn` | Desistiu voluntariamente |
| `Removed` | Removido por admin |
| `Absent` | Marcado ausente no pós-jogo |
| `Participated` | Presente após fechamento |

- Unique `(MatchId, PlayerProfileId)` ativo (não recriar duplicata; reabrir withdrawn conforme política).
- Capacidade = `Match.PlayerLimit`. Contagem de `Confirmed` ≤ limite.
- **Concorrência:** inscrição da última vaga e promoção usam transação + lock da partida (ou `UPDLOCK`/`RowVersion`).

## 4. Prioridade de mensalistas

1. Mensalista **ativo** no grupo.
2. Pagamento do mês: `Paid`, `Exempt` ou `Pending` mantêm elegibilidade; `Overdue`/`Cancelled` perdem.
3. Na inscrição, mensalistas recebem badge `Monthly` (avulsos `Casual`).
4. Capacidade: quem chega primeiro com vaga livre confirma (FIFO).
5. Na **promoção da fila**, mensalistas elegíveis sobem antes de avulsos (depois ordem da fila).

> Escala ~50 usuários: sem janela de quinta-feira automatizada — o admin controla abertura da lista.

## 5. Fila e promoção

- Fila ordenada por `WaitlistPosition` crescente.
- Ao liberar vaga (`Withdrawn`/`Removed` de um `Confirmed`): promover o primeiro elegível da fila atomicamente.
- Elegibilidade na promoção: membro ativo do grupo; respeitar regras de inadimplência se aplicáveis.
- Nunca exceder `PlayerLimit`.

## 6. Mensalista ≠ pagamento

- `Membership`: vínculo (ativo/inativo/suspenso).
- `MembershipBillingPeriod`: competência (ex. 2026-08).
- `MembershipPayment`: lançamento com status `Paid | Pending | Overdue | Exempt | Cancelled`.

## 7. Performance e sorteio

- `SkillRating` no perfil = nível usado no sorteio (ajustável por admin).
- `PlayerMatchRating` = avaliação de uma partida (não substitui automaticamente o skill sem política explícita).
- **[H]** Sorteio: embaralhamento seeded + partição em N times minimizando diferença de soma de skills (busca local / greedy com random restarts). Persistido em `TeamDraw` com `Seed`.
- Refazer gera novo seed e novo registro (ou versão); reload da página não regenera.

## 8. Pós-jogo e estatísticas

- Fechamento único (`CloseMatch` use case) aplica presença, resultado, craque e ratings em transação.
- Rankings derivados de partidas `Completed` (consultas agregadas; materialização opcional depois).
- Aproveitamento sempre acompanhado de volume de partidas.
- **[H]** Desempate rankings: 1) métrica principal 2) nº de partidas 3) nome.

## 9. Auditoria

Operações admin sensíveis (pagamento, remoção, ajuste de sorteio, correção pós-jogo) geram `AuditEntry`.
