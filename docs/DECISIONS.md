# Decisions — Futebol Atemporal

Registro de decisões arquiteturais e de produto.

## ADR-001 — PRD vazio no repositório

- **Contexto:** `docs/PRD.md` estava vazio; não havia outro PRD versionado.
- **Decisão:** Sintetizar PRD e `DOMAIN_RULES.md` a partir do prompt de execução + sinais Stitch.
- **Consequência:** Reconciliar se um PRD oficial for entregue depois.

## ADR-002 — ZIP Stitch ausente

- **Contexto:** Apenas a pasta extraída existe em `references/stitch-export`.
- **Decisão:** Auditar a árvore extraída; documentar lacuna; não bloquear desenvolvimento.
- **Consequência:** Se o ZIP aparecer, preservá-lo em `references/` sem apagar a exportação.

## ADR-003 — Marca do produto

- **Contexto:** Stitch usa “PitchPerfect”; repositório/GitHub usam Futebol Atemporal / futebol-atamporal.
- **Decisão:** Nome de produto na UI = **Futebol Atemporal**; tokens visuais do DESIGN PitchPerfect Elite.
- **Consequência:** Strings “PitchPerfect” não entram em produção.

## ADR-004 — Tipografia Inter

- **Contexto:** Heurísticas genéricas desencorajam Inter; DESIGN.md exige Inter.
- **Decisão:** Manter Inter conforme design system do produto.

## ADR-005 — Stack greenfield

- **Contexto:** Sem código de aplicação existente.
- **Decisão:** Next.js (App Router) + ASP.NET Core Web API + EF Core + PostgreSQL + Docker Compose.
- **Consequência:** Monorepo `apps/web`, `apps/api`.

## ADR-006 — Autenticação por cookie

- **Contexto:** Requisito de não usar `localStorage` para tokens sensíveis.
- **Decisão:** ASP.NET Identity + cookie HTTP-only; frontend com `credentials: 'include'`.

## ADR-007 — Multi-grupo desde o dia 1

- **Contexto:** Produto começa com um grupo, mas hardcoding é arriscado.
- **Decisão:** Entidade `FootballGroup` + seed de um grupo; UI inicial usa o grupo padrão.

## ADR-008 — Prazo de prioridade mensalista

- **Contexto:** Stitch menciona “até as 18h de quinta-feira”.
- **Decisão:** Padrão configurável: quinta 18:00 no timezone do grupo (`DOMAIN_RULES` §4).

## ADR-009 — Algoritmo de sorteio

- **Contexto:** PRD exige equilíbrio + aleatoriedade + reprodutibilidade.
- **Decisão:** Seed persistida + partição com random restarts minimizando diferença de soma de skills.

## ADR-010 — Stitch não é produção

- **Contexto:** Exportação HTML/CDN estática.
- **Decisão:** REUSE tokens; ADAPT layouts; DISCARD HTML como código de app. Regenerar componentes na stack Next.

## ADR-011 — SQLite em Development local

- **Contexto:** Docker não estava disponível no ambiente de implementação; PostgreSQL via Compose permanece o alvo padrão.
- **Decisão:** `Database:UseSqlite=true` em `appsettings.Development.json` para rodar/testar sem containers; Compose e `appsettings.json` usam PostgreSQL.
- **Consequência:** Evitar `OrderBy` em `DateTimeOffset` em queries (limitação SQLite); preferir `OrderBy(Id)` ou materializar no cliente.

## ADR-012 — Cookie auth cross-origin em dev

- **Contexto:** Rewrites do Next não propagam cookies da API corretamente para o browser.
- **Decisão:** Frontend chama `NEXT_PUBLIC_API_URL` (padrão `http://localhost:5080`) com `credentials: 'include'` e CORS com origins explícitas.

## ADR-013 — Simplificação para ~50 usuários

- **Contexto:** App de um grupo casual; overengineering (audit write-only, rate-limit custom, draw com 48 restarts, prioridade de quinta que não afetava a fila, nav admin separada).
- **Decisão:** Snake-draft com seed; prioridade só na promoção da fila; authz 403; sem audit/rate-limit custom; nav única mobile; hub na home; dashboard admin redireciona.
- **Consequência:** Menos código, UX mais clara; tabelas de billing/audit permanecem no schema (billing em uso; audit sem escrita).
