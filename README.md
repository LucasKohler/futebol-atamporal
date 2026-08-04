# Futebol Atemporal

Web app mobile-first para peladas semanais: lista e fila com prioridade de mensalistas, mensalidades, sorteio equilibrado, pós-jogo, rankings e histórico.

Pensado para grupos pequenos (**até ~50 usuários**): monólito modular simples, sem filas, caches distribuídos ou rate-limit artificial.

## Features

| Área | O que faz |
|------|-----------|
| Auth | Cadastro, login, cookie HTTP-only (`fa_auth`) |
| Partidas | CRUD, estados, inscrição/desistência, capacidade |
| Fila | Waitlist + promoção; mensalistas sobem primeiro |
| Mensalistas | Vínculo, competências e lançamentos de pagamento |
| Sorteio | Snake-draft seeded e equilibrado |
| Pós-jogo | Presença, placar, craque (MOTM), avaliações |
| Rankings | Classificação e histórico do jogador |
| Admin | Gestão da lista, mensalistas, sorteio e registro pós-jogo |

## Stack

- **Frontend:** Next.js 15 (App Router), TypeScript, Tailwind CSS
- **Backend:** ASP.NET Core (.NET 10), EF Core, Identity
- **Banco:** SQLite em Development · PostgreSQL via Docker Compose
- **Auth:** cookies cross-origin com `credentials: "include"`

## Estrutura

```
apps/web          Frontend (Next.js)
apps/api          Backend (solution .slnx)
docs/             PRD, design, arquitetura, regras, ADRs
references/       Exportação Stitch (referência visual)
docker-compose.yml
```

## Pré-requisitos

- .NET 10 SDK
- Node.js 20+ (22 recomendado) e npm
- Docker Desktop (opcional; Compose/PostgreSQL)
- WSL2 no Windows é recomendado para `dotnet` / scripts bash

## Subir em Development (sem Docker)

```bash
# API (SQLite + seed admin) — porta 5080
dotnet run --project apps/api/src/FutebolAtemporal.Api

# Web (outro terminal)
cd apps/web
cp ../../.env.example .env.local   # opcional; default já aponta para :5080
npm install
npm run dev
```

| Serviço | URL |
|---------|-----|
| Web | http://localhost:3000 |
| API | http://localhost:5080 |
| Health | http://localhost:5080/health |
| Swagger | http://localhost:5080/swagger |

**Admin seed:** `admin@futebolatemporal.local` / `Admin123!`

## Docker Compose

```bash
cp .env.example .env
docker compose up -d --build
```

Com Compose, a API usa PostgreSQL (`Database__UseSqlite=false`).

## Testes e qualidade

```bash
dotnet test apps/api/FutebolAtemporal.slnx
cd apps/web && npm run lint && npm run typecheck && npm run build
```

## Documentação

| Doc | Conteúdo |
|-----|----------|
| [docs/PRD.md](docs/PRD.md) | Requisitos de produto |
| [docs/DESIGN.md](docs/DESIGN.md) | Design system |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Arquitetura |
| [docs/DOMAIN_RULES.md](docs/DOMAIN_RULES.md) | Regras de negócio |
| [docs/DATA_MODEL.md](docs/DATA_MODEL.md) | Modelo de dados |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Fases (0–6 concluídas) |
| [docs/API.md](docs/API.md) | Contrato da API |
| [docs/DECISIONS.md](docs/DECISIONS.md) | ADRs |
| [docs/TESTING.md](docs/TESTING.md) | Estratégia de testes |
| [docs/STITCH_EXPORT_AUDIT.md](docs/STITCH_EXPORT_AUDIT.md) | Auditoria Stitch |
