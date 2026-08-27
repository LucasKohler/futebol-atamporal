# Testing — Futebol Atemporal

## Estratégia

Priorizar risco: regras de domínio e fluxos críticos antes de cobertura cosmética.

| Camada | Ferramenta (inicial) | Foco |
|--------|----------------------|------|
| Domain unit | xUnit | Prioridade, fila, sorteio, rankings |
| API integration | xUnit + WebApplicationFactory + Testcontainers/PostgreSQL | Auth, inscrição concorrente, fechamento |
| Frontend unit/component | Vitest + Testing Library | Componentes críticos |
| E2E | Playwright (Fase 6) | Fluxos do prompt §15 |

## Fase 1 (smoke)

- API sobe e `/health` retorna Healthy
- Registro (celular+nome) / login (só celular) / `me` com cookie
- Build Next.js e lint

## Prioridade de testes (fases seguintes)

1. Capacidade e última vaga concorrente
2. Ordenação/promoção da fila
3. Prioridade mensalista + prazo
4. Pagamento e elegibilidade
5. Sorteio seeded / persistência
6. Fechamento de partida e agregados
7. Isolamento entre grupos
8. Autorização admin vs jogador

## Comandos

```bash
dotnet test apps/api/FutebolAtemporal.slnx
cd apps/web && npm test && npm run lint && npm run build
```
