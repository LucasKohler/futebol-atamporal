# Architecture — Futebol Atemporal

## Visão geral

Monólito modular em monorepo:

```
apps/web     → Next.js (App Router) + TypeScript + Tailwind
apps/api     → ASP.NET Core Web API + EF Core + PostgreSQL
docs/        → documentação de produto e técnica
references/  → Stitch e materiais de design (não produção)
```

Docker Compose sobe `postgres`, `api` e `web` para desenvolvimento local.

## Diagrama

```
Browser → Next.js (RSC + Client) → ASP.NET API → Domain/Application → EF Core → PostgreSQL
```

## Frontend (`apps/web`)

- App Router, TypeScript strict
- Tailwind com tokens de `docs/DESIGN.md`
- Componentes estilo shadcn adaptados ao design system (não visual default)
- React Hook Form + Zod
- TanStack Query para estado de servidor
- Proxy `/api` → backend (mesmo origin em prod/dev quando possível)
- Cookies de sessão HTTP-only gerenciados pela API (credentials include)

## Backend (`apps/api`)

Camadas pragmáticas:

| Camada | Responsabilidade |
|--------|------------------|
| **Api** | Controllers, authz, OpenAPI, middleware, health |
| **Application** | Casos de uso (inscrição, promoção, sorteio, fechamento) |
| **Domain** | Entidades, enums, regras (prioridade, capacidade, rankings) |
| **Infrastructure** | EF Core, Identity, logging, seed |

Regras de negócio críticas vivem no Domain/Application — não em React, controllers finos apenas orquestram.

## Autenticação

- ASP.NET Core Identity
- Cookie authentication (HTTP-only, Secure em produção, SameSite adequado)
- CORS restrito à origem do web app
- Autorização por `FootballGroup` + role (`Player`, `Admin`)

## Persistência

- PostgreSQL
- Migrations versionadas (EF Core)
- Transações para inscrição/promoção/fechamento
- Concorrência: unique constraints + `RowVersion` / serialização onde necessário

## Observabilidade

- Logs estruturados (Serilog ou built-in + enrichment)
- Correlation ID no request pipeline
- `/health` (liveness) e checks de DB
- Exceções globais sem stack trace ao cliente

## Princípios

- Modular, testável, sem microsserviços prematuros
- Sem repositories genéricos que só encapsulam EF
- Feature completa = UI + API + persistência + authz + testes + docs
