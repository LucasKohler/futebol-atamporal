# API — Futebol Atemporal

Base: `http://localhost:5080` · Auth: cookie `fa_auth` · JSON camelCase · erros Problem Details

## Health

| Método | Path | Auth |
|--------|------|------|
| GET | `/health` | no |
| GET | `/health/ready` | no |

## Auth

| Método | Path | Descrição |
|--------|------|-----------|
| POST | `/api/auth/register` | Cadastro + cookie |
| POST | `/api/auth/login` | Login |
| POST | `/api/auth/logout` | Logout |
| GET | `/api/auth/me` | Usuário + memberships |

Rate limit: ~30 req/min por IP em login/register.

## Groups

| Método | Path |
|--------|------|
| GET | `/api/groups/current` |
| GET | `/api/groups/{groupId}` |

## Matches & registrations

| Método | Path |
|--------|------|
| POST | `/api/matches` |
| GET | `/api/matches?groupId=` |
| GET | `/api/matches/upcoming?groupId=` |
| GET | `/api/matches/{id}` |
| PUT | `/api/matches/{id}` |
| POST | `/api/matches/{id}/transition` |
| POST | `/api/matches/{id}/join` |
| POST | `/api/matches/{id}/leave` |
| POST | `/api/matches/{id}/registrations/{playerProfileId}/remove` |

## Memberships & payments

| Método | Path |
|--------|------|
| GET | `/api/groups/{groupId}/memberships` |
| POST | `/api/groups/{groupId}/memberships` |
| POST | `/api/groups/{groupId}/memberships/{id}/status` |
| POST | `/api/groups/{groupId}/memberships/periods` |
| GET | `/api/groups/{groupId}/memberships/payments` |
| PUT | `/api/groups/{groupId}/memberships/{id}/payments/{periodId}` |

## Draw & results

| Método | Path |
|--------|------|
| GET/POST | `/api/matches/{id}/draw` |
| POST | `/api/matches/{id}/draw/{drawId}/confirm` |
| POST | `/api/matches/{id}/draw/{drawId}/swap` |
| GET | `/api/matches/{id}/result` |
| POST | `/api/matches/{id}/result/close` |

## Rankings & history

| Método | Path |
|--------|------|
| GET | `/api/groups/{groupId}/rankings?sortBy=wins\|winrate\|rating\|motm` |
| GET | `/api/players/{playerProfileId}/history` |
