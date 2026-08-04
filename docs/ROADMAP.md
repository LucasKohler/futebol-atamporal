# Roadmap — Futebol Atemporal

Legenda: `todo` | `in_progress` | `done`

Uma feature só marca `done` com UI + API + persistência + authz + estados + testes críticos + docs (quando aplicável).

## Fase 0 — Descoberta e planejamento

| Item | Status |
|------|--------|
| Auditoria Stitch | done |
| PRD / DESIGN / DOMAIN / DATA_MODEL / ARCHITECTURE / DECISIONS | done |
| Roadmap completo | done |

## Fase 1 — Fundação

| Item | Status |
|------|--------|
| Monorepo web + api + Compose | done |
| Design tokens + layout shell | done |
| Identity + cookies + authz básica | done |
| FootballGroup + seed | done |
| Health checks + logs + OpenAPI | done |
| Testes smoke | done |
| README + .env.example | done |

## Fase 2 — Partidas e inscrições

| Item | Status |
|------|--------|
| CRUD partidas + estados | done |
| Página da partida / lista | done |
| Inscrição / desistência | done |
| Capacidade + concorrência | done |
| Prioridade mensalistas | done |
| Fila + promoção automática | done |
| Gestão admin da lista | done |
| Testes de domínio/integração | done |

## Fase 3 — Mensalistas e pagamentos

| Item | Status |
|------|--------|
| Vínculo mensalista | done |
| Competências | done |
| Lançamentos / filtros / histórico | done |
| Elegibilidade ligada a pagamento | done |
| Telas admin | done |

## Fase 4 — Performance e sorteio

| Item | Status |
|------|--------|
| Skill rating + avaliações | done |
| Algoritmo seeded equilibrado | done |
| Persistência / refazer / ajustes | done |
| Testes de distribuição | done |
| Telas sorteio | done |

## Fase 5 — Pós-jogo, rankings e histórico

| Item | Status |
|------|--------|
| Presença / placar / craque | done |
| Fechamento transacional | done |
| Rankings + desempate | done |
| Histórico jogador/partida | done |
| Dashboard operacional | done |

## Fase 6 — Consolidação

| Item | Status |
|------|--------|
| A11y + responsividade fina | done |
| Segurança (rate limit auth, cookies, authz) | done |
| Testes críticos (domínio + fluxo inscrição/fila) | done |
| Seeds dev + prep deploy (Compose/README) | done |
| Docs/API/roadmap atualizados | done |

## Critérios de conclusão global

App local executável, auth, lista/fila, pagamentos, sorteio persistido, resultados, rankings, mobile/desktop, testes críticos. Evoluções futuras: E2E Playwright completo, CSRF token explícito em proxies same-site, jobs de prazo de prioridade.
