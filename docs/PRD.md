# Product Requirements Document — Futebol Atemporal

> Documento sintetizado a partir do prompt de execução do produto e dos sinais de domínio na exportação Google Stitch, porque `docs/PRD.md` estava vazio no repositório. Se um PRD oficial for fornecido depois, este arquivo deve ser reconciliado e as divergências registradas em `DECISIONS.md`.

## 1. Visão

Web app responsivo e mobile-first para administrar partidas recorrentes de futebol casual (pelada), substituindo a organização manual em grupos de WhatsApp.

O produto centraliza: partidas semanais, inscrições, prioridade de mensalistas, fila de espera, mensalidades, perfil, avaliação de performance, sorteio equilibrado de times, registro pós-jogo, estatísticas, rankings e histórico.

## 2. Personas e papéis

| Papel | Descrição |
|-------|-----------|
| **Jogador** | Consulta próxima partida, entra/sai da lista, vê fila, perfil, rankings e histórico. |
| **Administrador** | Gerencia grupo, partidas, lista, mensalistas, pagamentos, sorteio, pós-jogo e pendências operacionais. |

Um usuário pode ser membro de um `FootballGroup` com um ou mais papéis. O acesso é sempre escopado ao grupo.

## 3. Escopo funcional

### 3.1 Autenticação e autorização

- Cadastro: celular + nome (nome imutável pelo jogador)
- Login: apenas o número de celular (normalizado, único; sem senha/e-mail/SMS)
- Sessão via cookies HTTP-only seguros
- Admin pode definir apelido fixo; a UI usa apelido se existir, senão o nome cadastrado
- Proteção de rotas no frontend e autorização no backend
- Usuários inativos/bloqueados não autenticam

### 3.2 Organização / grupo

- Modelo explícito de grupo (`FootballGroup`)
- Nome, regras básicas configuráveis, membros e responsáveis
- Mesmo com um único grupo inicial, evitar IDs hardcoded na aplicação

### 3.3 Partidas

Administrador cria/edita partidas (data, horário, local, limite de jogadores), abre/encerra inscrições, cancela, conclui, visualiza participantes e histórico.

**Estados de partida:** `Draft`, `RegistrationOpen`, `RegistrationClosed`, `Drawn`, `InProgress`, `AwaitingClose`, `Completed`, `Cancelled`.

### 3.4 Inscrição semanal

Jogador visualiza próxima partida, capacidade, vagas, próprio status, entra/sai da lista, vê confirmados e posição na fila.

**Estados de inscrição:** `Confirmed`, `Waitlisted`, `Withdrawn`, `Removed`, `Absent`, `Participated` (e ausência de inscrição = não inscrito).

### 3.5 Prioridade de mensalistas

Mensalistas elegíveis (ativos e adimplentes conforme regras) têm prioridade de vaga. Regras centralizadas no domínio. Prazo configurável por grupo (padrão Stitch: até 18h da quinta anterior à partida).

### 3.6 Fila de espera

Ao atingir capacidade, novas inscrições entram na fila ordenada. Desistência promove o próximo elegível de forma transacional, com controle de concorrência.

### 3.7 Mensalistas e pagamentos

Vínculo de mensalista ≠ competência ≠ pagamento. Estados de pagamento: `Paid`, `Pending`, `Overdue`, `Exempt`, `Cancelled`.

### 3.8 Perfil, performance e sorteio

Perfil com dados cadastrais e métricas derivadas. Avaliações pós-partida distintas do nível técnico usado no sorteio. Sorteio aleatório e equilibrado, com seed, persistência e possibilidade de refazer/ajustar.

### 3.9 Pós-jogo, rankings e histórico

Presença, ausência, placar, vencedor, craque do dia, encerramento transacional. Rankings de performance, vitórias, aproveitamento e craques, com volume de partidas e critérios de desempate. Histórico de partidas, inscrições, sorteios, resultados, pagamentos e auditoria admin.

### 3.10 Dashboard administrativo

Foco operacional: próxima partida, inscritos, vagas, fila, pagamentos pendentes, ações frequentes.

## 4. Requisitos não funcionais

- Mobile-first, adequado a tablet/desktop
- Acessibilidade base WCAG 2.2 AA quando razoável
- Segurança: authz no backend, validação, CSRF quando aplicável, rate limiting em endpoints sensíveis, secrets fora do repo
- Observabilidade: logs estruturados, correlation ID, health checks
- Testes priorizados para regras críticas (fila, prioridade, sorteio, fechamento, rankings)

## 5. Fora de escopo (inicial)

- App nativo iOS/Android
- Pagamentos online / gateway
- Multi-tenant SaaS comercial completo (multi-grupo é modelado, mas UX inicial foca um grupo)
- Notificações push nativas (podem evoluir depois)

## 6. Critérios de conclusão do produto

Ver `ROADMAP.md` e seção de critérios globais. Feature só está concluída com UI + API + persistência + authz + estados + testes críticos + docs.
