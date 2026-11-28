import { readFileSync } from "node:fs";
import { join } from "node:path";
import vm from "node:vm";
import { describe, expect, it } from "vitest";

/** Executa public/sw.js num sandbox com um `self` falso e captura os listeners (CA-12). */
function loadWorker(clients: { url: string; focused?: boolean; navigatedTo?: string }[] = []) {
  const listeners: Record<string, (e: unknown) => void> = {};
  const shown: { title: string; options: Record<string, unknown> }[] = [];
  const opened: string[] = [];
  const self = {
    location: { origin: "https://fut.example" },
    addEventListener: (type: string, fn: (e: unknown) => void) => (listeners[type] = fn),
    skipWaiting: () => undefined,
    registration: {
      showNotification: (title: string, options: Record<string, unknown>) => {
        shown.push({ title, options });
        return Promise.resolve();
      },
    },
    clients: {
      claim: () => Promise.resolve(),
      matchAll: () =>
        Promise.resolve(
          clients.map((c) => ({
            url: c.url,
            focus() {
              c.focused = true;
              return Promise.resolve(this);
            },
            navigate(u: string) {
              c.navigatedTo = u;
              return Promise.resolve(this);
            },
          })),
        ),
      openWindow: (u: string) => {
        opened.push(u);
        return Promise.resolve(null);
      },
    },
    __faSw: undefined as unknown,
  };
  const code = readFileSync(join(__dirname, "../../public/sw.js"), "utf8");
  vm.runInNewContext(code, { self, URL, console });
  const fire = async (type: string, event: Record<string, unknown>) => {
    let p: Promise<unknown> = Promise.resolve();
    listeners[type]({ ...event, waitUntil: (x: Promise<unknown>) => (p = x) });
    await p;
  };
  return { listeners, shown, opened, fire, clients };
}

const ID = "0f8fad5b-d9cb-469f-a165-70867728950e";

describe("service worker: só push e clique (D7)", () => {
  it("não registra handler de fetch nem cache", () => {
    const { listeners } = loadWorker();
    expect(Object.keys(listeners).sort()).toEqual(["activate", "install", "notificationclick", "push"]);
  });

  it("push mostra título/corpo/ícone com tag e guarda id e url", async () => {
    const w = loadWorker();
    await w.fire("push", { data: { json: () => ({ id: ID, title: "⚽ Lista liberada", body: "Confirme", url: "/partida", tag: "MatchOpen-x" }) } });
    expect(w.shown[0].title).toBe("⚽ Lista liberada");
    expect(w.shown[0].options).toMatchObject({ body: "Confirme", tag: "MatchOpen-x", icon: "/icons/icon-192.png", data: { id: ID, url: "/partida" } });
  });

  it("payload inválido ou gigante vira texto limitado com título padrão", async () => {
    const w = loadWorker();
    await w.fire("push", { data: { json: () => ({ title: 5, body: "x".repeat(900) }) } });
    expect(w.shown[0].title).toBe("Futebol Atemporal");
    expect((w.shown[0].options.body as string).length).toBe(500);
  });

  it("clique foca a aba do app e navega para a rota permitida com ?n=", async () => {
    const w = loadWorker([{ url: "https://fut.example/rankings" }]);
    await w.fire("notificationclick", { notification: { close: () => undefined, data: { id: ID, url: "/partida" } } });
    expect(w.clients[0].focused).toBe(true);
    expect(w.clients[0].navigatedTo).toBe(`https://fut.example/partida?n=${ID}`);
  });

  it("sem aba aberta, abre janela; URL externa ou fora da lista vai para / (payload adulterado)", async () => {
    for (const evil of ["https://evil.com/x", "//evil.com", "javascript:alert(1)", "/admin", "/partida?x=1"]) {
      const w = loadWorker();
      await w.fire("notificationclick", { notification: { close: () => undefined, data: { id: ID, url: evil } } });
      expect(w.opened[0]).toBe(`https://fut.example/?n=${ID}`);
    }
  });

  it("id que não é GUID não entra na URL", async () => {
    const w = loadWorker();
    await w.fire("notificationclick", { notification: { close: () => undefined, data: { id: "<x>", url: "/perfil" } } });
    expect(w.opened[0]).toBe("https://fut.example/perfil");
  });
});
