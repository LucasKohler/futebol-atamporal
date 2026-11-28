/**
 * Catálogo declarativo do Painel de Admin.
 *
 * Para acoplar um novo módulo (ex.: regras do grupo, caixa, auditoria), basta adicionar uma entrada em
 * ADMIN_MODULES e criar a rota em `app/admin/<rota>/page.tsx` usando `AdminShell moduleId="<id>"`.
 * O menu e a grade do hub são gerados daqui. A API continua validando a autorização de cada chamada:
 * esconder um módulo no menu não é controle de acesso.
 */

export type AdminModuleGroup = "rodada" | "pessoas" | "financeiro" | "grupo";

export type AdminModuleIcon = "list" | "shuffle" | "scoreboard" | "people" | "wallet" | "settings" | "bell";

export type AdminModule = {
  id: string;
  title: string;
  /** Uma frase curta para o cartão do hub. */
  description: string;
  href: string;
  icon: AdminModuleIcon;
  /** Ordem dentro do grupo (menor primeiro). */
  order: number;
  group: AdminModuleGroup;
  /** Etapa do stepper da rodada que o módulo atende (1 a 4), quando houver. */
  roundStep?: 1 | 2 | 3 | 4;
  /** Rotas antigas ou relacionadas que também ativam o módulo no menu. */
  aliases?: string[];
};

export const ADMIN_GROUPS: Record<AdminModuleGroup, { title: string; order: number }> = {
  rodada: { title: "Rodada semanal", order: 1 },
  pessoas: { title: "Pessoas", order: 2 },
  financeiro: { title: "Financeiro", order: 3 },
  grupo: { title: "Grupo", order: 4 },
};

export const ADMIN_MODULES: AdminModule[] = [
  {
    id: "lista",
    title: "Lista & Presença",
    description: "Abrir e encerrar inscrições, confirmados e fila.",
    href: "/partida",
    icon: "list",
    order: 1,
    group: "rodada",
    roundStep: 1,
  },
  {
    id: "sorteio",
    title: "Sorteio 3 Cores",
    description: "Vermelho, Preto e Verde equilibrados, trocas e escalação para o WhatsApp.",
    href: "/admin/sorteio",
    icon: "shuffle",
    order: 2,
    group: "rodada",
    roundStep: 2,
  },
  {
    id: "registro",
    title: "Súmula do dia",
    description: "Partidas ao vivo, pênaltis e 🧤, checagem do dia e Melhores do dia no fim da pelada.",
    href: "/admin/registro",
    icon: "scoreboard",
    order: 3,
    group: "rodada",
    roundStep: 4,
  },
  {
    id: "elenco",
    title: "Elenco & Notas",
    description: "Apelidos, notas de linha e de goleiro, um jogador por vez.",
    href: "/admin/elenco",
    icon: "people",
    order: 1,
    group: "pessoas",
  },
  {
    id: "mensalidades",
    title: "Mensalidades & Pix",
    description: "Mensalidades, diárias dos avulsos, transações Pix do Mercado Pago e vínculos.",
    href: "/admin/mensalistas",
    icon: "wallet",
    order: 1,
    group: "financeiro",
  },
  {
    id: "notificacoes",
    title: "Notificações",
    description: "Lembretes rápidos, comunicados e avisos enviados ao celular e ao sininho do app.",
    href: "/admin/notificacoes",
    icon: "bell",
    order: 1,
    group: "grupo",
  },
];

/** Problemas do registro (ids/rotas repetidos, etapa inválida). Lista vazia = registro válido. */
export function validateAdminModules(modules: AdminModule[]): string[] {
  const issues: string[] = [];
  const ids = new Set<string>();
  const hrefs = new Set<string>();
  for (const m of modules) {
    if (!m.id.trim()) issues.push("Módulo sem id.");
    if (ids.has(m.id)) issues.push(`Id repetido: ${m.id}.`);
    if (hrefs.has(m.href)) issues.push(`Rota repetida: ${m.href}.`);
    if (!m.href.startsWith("/")) issues.push(`Rota relativa em ${m.id}.`);
    if (m.roundStep !== undefined && ![1, 2, 3, 4].includes(m.roundStep)) issues.push(`Etapa inválida em ${m.id}.`);
    ids.add(m.id);
    hrefs.add(m.href);
  }
  return issues;
}

/** Módulos ordenados por grupo e depois pela ordem interna. */
export function sortAdminModules(modules: AdminModule[]): AdminModule[] {
  return [...modules].sort(
    (a, b) =>
      ADMIN_GROUPS[a.group].order - ADMIN_GROUPS[b.group].order || a.order - b.order || a.title.localeCompare(b.title, "pt-BR"),
  );
}

/** Seções do hub: grupos que têm ao menos um módulo, na ordem definida. */
export function groupAdminModules(modules: AdminModule[]) {
  const sorted = sortAdminModules(modules);
  return (Object.keys(ADMIN_GROUPS) as AdminModuleGroup[])
    .sort((a, b) => ADMIN_GROUPS[a].order - ADMIN_GROUPS[b].order)
    .map((group) => ({ group, title: ADMIN_GROUPS[group].title, modules: sorted.filter((m) => m.group === group) }))
    .filter((section) => section.modules.length > 0);
}

/** Módulo ativo para a rota atual (rota exata, sub-rota ou alias). */
export function findAdminModule(pathname: string, modules: AdminModule[] = ADMIN_MODULES): AdminModule | undefined {
  const matches = (path: string) => pathname === path || pathname.startsWith(`${path}/`);
  return modules.find((m) => matches(m.href) || (m.aliases ?? []).some(matches));
}

/** Item ativo da navegação principal: rota exata, ou qualquer rota /admin* para o item Admin. */
export function isNavActive(pathname: string, href: string): boolean {
  if (href === "/admin") return pathname === "/admin" || pathname.startsWith("/admin/");
  return pathname === href;
}

export function getAdminModule(id: string, modules: AdminModule[] = ADMIN_MODULES): AdminModule | undefined {
  return modules.find((m) => m.id === id);
}
