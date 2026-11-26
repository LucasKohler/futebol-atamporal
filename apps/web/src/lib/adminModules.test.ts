import { describe, expect, it } from "vitest";
import {
  ADMIN_MODULES,
  findAdminModule,
  groupAdminModules,
  isNavActive,
  sortAdminModules,
  validateAdminModules,
  type AdminModule,
} from "./adminModules";

describe("AdminModuleRegistry", () => {
  it("registro atual é válido e cobre as rotas administrativas existentes", () => {
    expect(validateAdminModules(ADMIN_MODULES)).toEqual([]);
    const hrefs = ADMIN_MODULES.map((m) => m.href);
    for (const route of ["/partida", "/admin/sorteio", "/admin/registro", "/admin/elenco", "/admin/mensalistas"]) {
      expect(hrefs).toContain(route);
    }
  });

  it("módulo fictício entra só com uma entrada no registro e aparece no grupo certo", () => {
    const caixa: AdminModule = {
      id: "caixa",
      title: "Caixa",
      description: "Entradas e saídas (exemplo fictício).",
      href: "/admin/caixa",
      icon: "wallet",
      order: 2,
      group: "financeiro",
    };
    const modules = [...ADMIN_MODULES, caixa];
    expect(validateAdminModules(modules)).toEqual([]);
    const financeiro = groupAdminModules(modules).find((s) => s.group === "financeiro");
    expect(financeiro?.modules.map((m) => m.id)).toEqual(["mensalidades", "caixa"]);
    expect(findAdminModule("/admin/caixa/lancamentos", modules)?.id).toBe("caixa");
  });

  it("detecta id e rota repetidos e rota relativa", () => {
    const dup = { ...ADMIN_MODULES[0] };
    const relative = { ...ADMIN_MODULES[1], id: "x", href: "admin/x" };
    const issues = validateAdminModules([...ADMIN_MODULES, dup, relative]);
    expect(issues.some((i) => i.startsWith("Id repetido"))).toBe(true);
    expect(issues.some((i) => i.startsWith("Rota repetida"))).toBe(true);
    expect(issues.some((i) => i.startsWith("Rota relativa"))).toBe(true);
  });

  it("ordena por grupo (rodada, pessoas, financeiro) e ordem interna; grupos vazios somem", () => {
    expect(sortAdminModules(ADMIN_MODULES).map((m) => m.id)).toEqual(["lista", "sorteio", "registro", "elenco", "mensalidades"]);
    expect(groupAdminModules(ADMIN_MODULES).map((s) => s.group)).toEqual(["rodada", "pessoas", "financeiro"]);
  });

  it("item Admin da navegação fica ativo em qualquer rota /admin*", () => {
    expect(isNavActive("/admin", "/admin")).toBe(true);
    expect(isNavActive("/admin/sorteio", "/admin")).toBe(true);
    expect(isNavActive("/administracao", "/admin")).toBe(false);
    expect(isNavActive("/partida", "/partida")).toBe(true);
    expect(isNavActive("/partida/x", "/partida")).toBe(false);
  });
});
