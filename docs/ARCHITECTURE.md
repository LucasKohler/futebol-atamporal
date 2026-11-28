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

## Sessões e entrega Web Push

- O cookie tem validade fixa de 14 dias. A limpeza considera o vencimento original, inclusive para sessões revogadas antes dele. Esta política assume que não há uma exigência de auditoria para guardar a linha de sessão após o vencimento. O despachante tenta limpar na primeira passagem e depois a cada hora, em lotes de até 250. A transação desativa e desvincula inscrições dessas sessões, preserva registros de aparelhos e histórico de entregas e remove as linhas de sessão. Uma nova sessão pode reativar o mesmo endpoint.
- Antes de cada nova chamada ao provedor, o despachante revalida vínculo, inscrição e sessão. Se a revogação já foi observada, a nova chamada é suprimida. Uma chamada já iniciada pode terminar após a revogação, e uma mensagem já aceita pelo provedor pode chegar ao aparelho. A aplicação não consegue recolhê-la.
- Os logs do cliente de push ficam desabilitados e os erros de envio guardam apenas o identificador interno e o tipo da exceção. O handler do .NET 10 omite os spans HTTP desse cliente, pois o caminho da URL também pode conter o token do aparelho. O EventSource de diagnóstico `System.Net.Http` ainda pode capturar esse caminho se for habilitado externamente. Mantenha essa coleta fora dos sinks de produção para push, ou filtre os campos antes da exportação. [Diagnóstico HTTP no .NET 10](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/System.Net.Http/src/System/Net/Http/HttpTelemetry.cs)
- O socket conecta ao IP filtrado após o DNS, com HTTPS na porta 443 e nomes de provedores permitidos. IPv4 mapeado em IPv6 passa pelo filtro IPv4. IPv6 aceita unicast global em `2000::/3` fora dos prefixos especiais bloqueados para Web Push, como documentação e transição. [Registro de endereços especiais IPv6 da IANA](https://www.iana.org/assignments/iana-ipv6-special-registry)

## Princípios

- Modular, testável, sem microsserviços prematuros
- Sem repositories genéricos que só encapsulam EF
- Feature completa = UI + API + persistência + authz + testes + docs
