# Stitch Export Audit

## 1. Localização do ZIP

**Não encontrado** no repositório. Nenhuma pasta contém `stitch-export.zip` ou equivalente.

## 2. Arquivos extraídos

```
references/stitch-export/stitch_pelada_pro_manager/stitch_pelada_pro_manager/
```

Preservar esta árvore como referência. Se um ZIP for adicionado depois, colocá-lo em `references/` sem apagar a exportação atual.

## 3. Stack detectada

| Camada | Tecnologia |
|--------|------------|
| Framework | Nenhum (HTML estático) |
| CSS | Tailwind via CDN (`cdn.tailwindcss.com`) |
| Fontes | Inter (Google Fonts) |
| Ícones | Material Symbols Outlined |
| JS | Vanilla mínimo (sorteio toggle, filtro mensalistas) |

## 4. Estrutura de diretórios

Cada tela = pasta com `code.html` + `screen.png`. Pasta `pitch_performance/` contém apenas `DESIGN.md`.

## 5–7. Páginas, rotas e componentes

Não há rotas reais (`href="#"`). Componentes implícitos repetidos: TopAppBar, BottomNavBar, AdminSidebar, MetricCard, StatusChip, PlayerRow, PrimaryCTA, FAB, InfoBanner.

### Telas

| Pasta | Função |
|-------|--------|
| `home_do_jogador_consolidada_mobile` | Home do jogador (PT-BR) |
| `home_do_jogador_consolidada_desktop` | Home desktop (EN) |
| `dashboard_admin_consolidado_desktop` | Dashboard admin desktop |
| `dashboard_admin_mobile` | Dashboard admin mobile |
| `lista_da_partida_consolidada_mobile` | Lista + fila |
| `sorteio_de_times_consolidado_mobile` | Sorteio |
| `gest_o_de_mensalistas_consolidada_mobile` | Mensalistas/pagamentos |
| `registro_p_s_jogo_mobile` | Pós-jogo |
| `perfil_do_jogador_mobile` | Perfil |
| `estat_sticas_e_rankings_desktop` | Rankings |

## 8–9. Componentes e versões repetidas

- Home: mobile PT-BR (oficial) vs desktop EN (referência)
- Dashboard: desktop (oficial base) vs mobile (merge em layout responsivo)

## 10–13. Assets, fontes, tokens, libs

- Assets: avatars remotos `lh3.googleusercontent.com`; `screen.png` locais
- Fontes: Inter; Material Symbols
- Tokens: ver `docs/DESIGN.md` (cópia do Stitch DESIGN.md)
- Dependências: só CDN — **não** reutilizar no app de produção

## 14–16. Mocks, botões e formulários

Quase tudo é decorativo. Exceções demo: toggle de views no sorteio (não embaralha); filtro + “pago agora” fake em mensalistas. Formulários sem submit real.

## 17–20. Qualidade

- HTML monolítico por tela, configs Tailwind duplicadas
- Estilos inline ocasionais em ícones
- A11y fraca: poucos `aria-*`, `data-alt` em vez de `alt`, nav em `div`s
- Responsividade via exports separados, não um layout adaptativo único

## 21–22. Inconsistências

| Fonte | Conflito |
|-------|----------|
| DESIGN vs HTML desktop home | Primary drift `#065f46` vs `#002d1f` |
| Stitch vs produto | Brand “PitchPerfect” vs “Futebol Atemporal” |
| Stitch vs PRD | Telas estáticas ≠ features; EN no desktop home |
| Rankings HTML | Cores one-off fora dos tokens |

## 23–26. Classificação

| Elemento | Classe | Justificativa |
|----------|--------|---------------|
| `DESIGN.md` tokens/princípios | **REUSE** | Base do design system |
| Home jogador mobile | **ADAPT** | IA clara PT-BR |
| Lista da partida | **ADAPT** | Fluxo core confirmados/fila/FAB |
| Sorteio | **ADAPT** | Dois passos úteis; JS demo descartável |
| Mensalistas | **ADAPT** | Padrão admin de pagamentos |
| Registro pós-jogo | **ADAPT** | Melhor estrutura de form |
| Perfil | **ADAPT** | Stats + histórico |
| Dashboard admin desktop | **ADAPT** | Shell + métricas |
| Rankings desktop | **ADAPT** | Tabela; limpar cores fora do token |
| Dashboard admin mobile | **REFERENCE** | Merge no dashboard responsivo |
| Home desktop EN | **REFERENCE** | Idioma/roles misturados |
| `screen.png` | **REFERENCE** | QA visual |
| HTML CDN / mocks / JS demo | **DISCARD** (produção) | Regenerar na stack real |

### Versões oficiais por feature

1. Home → mobile consolidada
2. Lista → mobile consolidada
3. Admin → desktop consolidado (responsivo)
4. Mensalistas / Sorteio / Pós-jogo / Perfil → mobile consolidada
5. Rankings → desktop (adaptar mobile)
