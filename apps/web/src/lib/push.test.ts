import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { canLogoutAfter } from "./notifications";

/**
 * push.ts com navegador, Web Locks e API simulados. Cada `loadTab()` é uma instância nova do módulo,
 * como uma aba: compartilham localStorage, Web Locks e o servidor, mas não a memória do módulo.
 * Funções simples (não vi.fn): uma rejeição devolvida por vi.fn é registrada pelo Vitest como erro do teste.
 */
const log: string[] = [];
let unsubscribeImpl: () => Promise<void> = () => Promise.resolve();
let subscribeImpl: (endpoint: string) => Promise<{ keyId: string }> = () => Promise.resolve({ keyId: "k1" });
/** Usuário da sessão no servidor (cookie compartilhado entre as abas). null = sem sessão. */
let sessionUser: string | null = "u1";
/** "fail": a consulta de sessão falha (rede/servidor). */
let meMode: "normal" | "fail" = "normal";
/** Corpos enviados ao POST de inscrição (para conferir `expectedUserId`). */
const putBodies: Array<{ endpoint: string; expectedUserId?: string }> = [];
/** Troca o cookie para esta conta logo DEPOIS de responder a N-ésima consulta de sessão (janela GET→POST). */
let switchAfterMe: { call: number; to: string } | null = null;
let meCalls = 0;
/** Corpos enviados ao DELETE de inscrição (para conferir `expectedUserId`). */
const deleteBodies: Array<{ endpoint: string; expectedUserId?: string }> = [];
/** Conta capturada pela tela no toque: a do cookie naquele momento (como `useSession`). */
const actor = () => sessionUser ?? "u1";

vi.mock("./api", () => {
  class ApiError extends Error {
    status: number;
    code: string | null;
    constructor(message: string, status: number, code: string | null = null) {
      super(message);
      this.status = status;
      this.code = code;
    }
  }
  return {
    ApiError,
    api: {
      unsubscribePush: async (endpoint: string, expectedUserId?: string) => {
        deleteBodies.push({ endpoint, expectedUserId });
        // Precondição do backend: só remove se a conta esperada for a do cookie; senão 409 sem remover nada.
        if (!expectedUserId || expectedUserId !== sessionUser) {
          log.push(`DELETE-rejected ${endpoint}`);
          throw new ApiError("A sessão mudou antes de concluir a operação.", 409, "session_changed");
        }
        log.push(`DELETE ${endpoint}`);
        return unsubscribeImpl();
      },
      subscribePush: async (body: { endpoint: string; expectedUserId?: string }) => {
        putBodies.push({ ...body });
        // Precondição do backend: a conta esperada precisa ser a do cookie; senão 409 sem gravar.
        if (!body.expectedUserId || body.expectedUserId !== sessionUser) {
          log.push(`PUT-rejected ${body.endpoint}`);
          throw new ApiError("A sessão mudou antes de ativar as notificações.", 409, "session_changed");
        }
        log.push(`PUT-start ${body.endpoint}`);
        const r = await subscribeImpl(body.endpoint);
        log.push(`PUT-done ${body.endpoint} as ${sessionUser}`);
        return r;
      },
      vapidPublicKey: () => Promise.resolve({ publicKey: "AQAB", keyId: "k2" }),
      me: () => {
        if (meMode === "fail") return Promise.reject(new Error("rede"));
        meCalls++;
        const answer = sessionUser ? { id: sessionUser } : null;
        if (switchAfterMe && meCalls === switchAfterMe.call) sessionUser = switchAfterMe.to; // outra aba faz login
        return Promise.resolve(answer);
      },
    },
  };
});

type Sub = { endpoint: string; unsubscribe: () => Promise<boolean>; toJSON: () => object; getKey: () => null };

function makeSub(endpoint: string, unsubscribe: () => Promise<boolean> = () => Promise.resolve(true)): Sub {
  return { endpoint, unsubscribe, toJSON: () => ({ keys: { p256dh: "p", auth: "a" } }), getKey: () => null };
}

/** Web Locks simulada: exclusiva, fila FIFO, comum a todas as "abas"; respeita `signal` antes de começar. */
function makeLocks(opts: { noQuery?: boolean } = {}) {
  // Fila FIFO exclusiva POR NOME (como a Web Locks real), comum a todas as "abas"; registra quem segura e quem espera.
  const tails = new Map<string, Promise<unknown>>();
  const held = new Map<string, number>();
  const pending = new Map<string, number>();
  const gates = new Map<string, Promise<void>[]>();
  const bump =(m: Map<string, number>, k: string, d: number) => {
    const v = (m.get(k) ?? 0) + d;
    if (v <= 0) m.delete(k);
    else m.set(k, v);
  };
  const api = {
    request(name: string, a: unknown, b?: unknown) {
      const fn = (typeof a === "function" ? a : b) as (l: { name: string }) => Promise<unknown>;
      const o = (typeof a === "function" ? {} : a) as { signal?: AbortSignal };
      let started = false;
      let abortReject: ((e: unknown) => void) | null = null;
      const aborted = new Promise<never>((_, reject) => (abortReject = reject));
      o.signal?.addEventListener("abort", () => {
        if (!started) {
          bump(pending, name, -1);
          abortReject?.(new DOMException("Aborted", "AbortError"));
        }
      });
      bump(pending, name, 1);
      const tail = tails.get(name) ?? Promise.resolve();
      const run = tail.then(async () => {
        if (o.signal?.aborted) throw new DOMException("Aborted", "AbortError");
        started = true;
        bump(pending, name, -1);
        bump(held, name, 1);
        try {
          return await fn({ name });
        } finally {
          bump(held, name, -1);
        }
      });
      tails.set(name, run.catch(() => undefined));
      const result = o.signal ? Promise.race([run, aborted]) : run;
      // Janela permitida pela especificação: o bloqueio já foi liberado, mas quem chamou ainda não retomou.
      const gate = gates.get(name)?.shift();
      if (!gate) return result;
      return result.then(
        async (v) => (await gate, v),
        async (e) => {
          await gate;
          throw e;
        },
      );
    },
    query: async () => ({
      held: [...held.keys()].map((name) => ({ name })),
      pending: [...pending.keys()].map((name) => ({ name })),
    }),
  };
  /** Simula o fechamento de uma aba: solta os bloqueios mantidos cujo nome começa com `prefix`. */
  const dropHeld = (prefix: string) => {
    for (const k of [...held.keys()]) if (k.startsWith(prefix)) held.delete(k);
  };
  /** Nomes dos bloqueios mantidos agora (para conferir que nenhum marcador foi criado). */
  const heldNames = () => [...held.keys()];
  /** O próximo pedido de `name` só devolve o resultado a quem chamou depois de `release()` (bloqueio já solto). */
  const gateNextResult = (name: string) => {
    let release!: () => void;
    const p = new Promise<void>((r) => (release = r));
    gates.set(name, [...(gates.get(name) ?? []), p]);
    return { release };
  };
  if (opts.noQuery) return { request: api.request, dropHeld, heldNames, gateNextResult };
  return { ...api, dropHeld, heldNames, gateNextResult };
}

/** BroadcastChannel simulado, comum às "abas"; entrega assíncrona às outras instâncias, como o real. */
function makeBroadcastBus() {
  const instances: Array<{ name: string; deaf: boolean; listeners: Set<(ev: { data: unknown }) => void> }> = [];
  const sent: unknown[] = [];
  class FakeBroadcastChannel {
    private self: { name: string; deaf: boolean; listeners: Set<(ev: { data: unknown }) => void> };
    constructor(name: string) {
      this.self = { name, deaf: false, listeners: new Set() };
      instances.push(this.self);
    }
    addEventListener(_t: string, fn: (ev: { data: unknown }) => void) {
      this.self.listeners.add(fn);
    }
    removeEventListener(_t: string, fn: (ev: { data: unknown }) => void) {
      this.self.listeners.delete(fn);
    }
    postMessage(data: unknown) {
      sent.push(structuredClone(data));
      for (const other of instances) {
        if (other === this.self || other.name !== this.self.name) continue;
        setTimeout(() => {
          if (other.deaf) return;
          for (const fn of [...other.listeners]) fn({ data: structuredClone(data) });
        }, 0);
      }
    }
    close() {}
  }
  /** Deixa a última aba criada sem responder (ex.: aba congelada pelo navegador). */
  const deafenLast = () => {
    const last = instances[instances.length - 1];
    if (last) last.deaf = true;
  };
  return { FakeBroadcastChannel, deafenLast, sent: () => [...sent] };
}

type BrowserOpts = {
  sub: Sub | null;
  getSubscription?: () => Promise<Sub | null>;
  getRegistrationFails?: boolean;
  noRegistration?: boolean;
  registerFails?: boolean;
  withLocks?: boolean;
  /** "setThrows": gravação recusada (cota cheia) com leitura funcionando; "setNoop": gravação some em silêncio. */
  storage?: "normal" | "setThrows" | "setNoop" | "allThrow";
  noLockQuery?: boolean;
  noBroadcast?: boolean;
  requestPermission?: () => Promise<NotificationPermission>;
};

function setupBrowser(opts: BrowserOpts) {
  const created = { count: 0, unsubscribed: 0 };
  const permissionRequests = { count: 0 };
  const store = new Map<string, string>([["fa.push.keyId", "k1"]]);
  const reg = {
    pushManager: {
      getSubscription: opts.getSubscription ?? (() => Promise.resolve(opts.sub)),
      subscribe: () => {
        created.count++;
        return Promise.resolve(
          makeSub("https://push.example/novo", () => {
            created.unsubscribed++;
            return Promise.resolve(true);
          }),
        );
      },
    },
  };
  let registerCalls = 0;
  vi.stubGlobal("window", { Notification: {}, PushManager: {} });
  const bus = makeBroadcastBus();
  vi.stubGlobal("BroadcastChannel", opts.noBroadcast ? undefined : bus.FakeBroadcastChannel);
  const locks = opts.withLocks === false ? undefined : makeLocks({ noQuery: opts.noLockQuery });
  const requestPermission = opts.requestPermission ?? (() => Promise.resolve("granted" as NotificationPermission));
  vi.stubGlobal("Notification", {
    permission: "granted",
    requestPermission: () => {
      permissionRequests.count++;
      return requestPermission();
    },
  });
  vi.stubGlobal("navigator", {
    userAgent: "teste",
    locks,
    serviceWorker: {
      register: () => {
        registerCalls++;
        return opts.registerFails ? Promise.reject(new Error("registro falhou")) : Promise.resolve(reg);
      },
      getRegistration: () =>
        opts.getRegistrationFails ? Promise.reject(new Error("sw indisponível")) : Promise.resolve(opts.noRegistration ? undefined : reg),
    },
  });
  vi.stubGlobal("localStorage", {
    getItem: (k: string) => {
      if (opts.storage === "allThrow") throw new DOMException("acesso negado", "SecurityError");
      return store.get(k) ?? null;
    },
    setItem: (k: string, v: string) => {
      if (opts.storage === "setThrows") throw new DOMException("cota excedida", "QuotaExceededError");
      if (opts.storage === "allThrow") throw new DOMException("acesso negado", "SecurityError");
      if (opts.storage === "setNoop") return;
      store.set(k, v);
    },
    removeItem: (k: string) => {
      if (opts.storage === "allThrow") throw new DOMException("acesso negado", "SecurityError");
      store.delete(k);
    },
  });
  return { store, registerCalls: () => registerCalls, locks, bus, created, permissionRequests };
}

/** Uma "aba": instância nova do módulo com o mesmo navegador/servidor. */
async function loadTab() {
  vi.resetModules();
  return await import("./push");
}

function deferred<T>() {
  let resolve!: (v: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}

const tick = () => new Promise((r) => setTimeout(r, 0));
/** Última gravação no servidor deve ser o DELETE: nenhum PUT termina depois dele. */
function lastServerWrite() {
  return [...log].reverse().find((l) => l.startsWith("DELETE ") || l.startsWith("PUT-done"))?.replace(/ as \S+$/, "");
}

beforeEach(() => {
  log.length = 0;
  sessionUser = "u1";
  meMode = "normal";
  putBodies.length = 0;
  deleteBodies.length = 0;
  switchAfterMe = null;
  meCalls = 0;
  unsubscribeImpl = () => Promise.resolve();
  subscribeImpl = () => Promise.resolve({ keyId: "k1" });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("disablePush (revogação real, sem esconder falhas)", () => {
  it("sem registro do service worker neste aparelho: nada a revogar", async () => {
    setupBrowser({ sub: null, noRegistration: true });
    expect(await (await loadTab()).disablePush(actor())).toEqual({ status: "none" });
    expect(log).toEqual([]);
  });

  it("registro existe mas sem inscrição: nada a revogar", async () => {
    setupBrowser({ sub: null });
    expect(await (await loadTab()).disablePush(actor())).toEqual({ status: "none" });
  });

  it("servidor e navegador confirmam: revogado, chave local removida e marca de desligado gravada", async () => {
    const { store } = setupBrowser({ sub: makeSub("https://push.example/1") });
    expect(await (await loadTab()).disablePush(actor())).toEqual({ status: "revoked" });
    expect(log).toEqual(["DELETE https://push.example/1"]);
    expect(store.has("fa.push.keyId")).toBe(false);
    expect(store.get("fa.push.optOut")).toBe("1");
  });

  it("erro de rede no DELETE: falha (server), inscrição local mantida e chave local preservada", async () => {
    const localUnsub = { calls: 0 };
    const { store } = setupBrowser({ sub: makeSub("e", async () => (localUnsub.calls++, true)) });
    unsubscribeImpl = () => Promise.reject(new Error("rede"));
    const r = await (await loadTab()).disablePush(actor());
    expect(r).toEqual({ status: "failed", reason: "server" });
    expect(canLogoutAfter(r)).toBe(false);
    expect(localUnsub.calls).toBe(0);
    expect(store.has("fa.push.keyId")).toBe(true);
  });

  it("falha ao ler o registro do service worker é falha, não ausência (bloqueia o logout)", async () => {
    setupBrowser({ sub: makeSub("e"), getRegistrationFails: true });
    const r = await (await loadTab()).disablePush(actor());
    expect(r).toEqual({ status: "failed", reason: "read" });
    expect(canLogoutAfter(r)).toBe(false);
  });

  it("falha ao ler a inscrição é falha, não ausência", async () => {
    setupBrowser({ sub: null, getSubscription: () => Promise.reject(new Error("pushManager")) });
    expect(await (await loadTab()).disablePush(actor())).toEqual({ status: "failed", reason: "read" });
  });

  it("revogar não depende de registrar o service worker: registro com falha não esconde a inscrição", async () => {
    const env = setupBrowser({ sub: makeSub("https://push.example/1"), registerFails: true });
    const push = await loadTab();
    await expect(push.registerServiceWorker()).rejects.toThrow("registro falhou");
    expect(await push.disablePush(actor())).toEqual({ status: "revoked" });
    expect(env.registerCalls()).toBe(1);
  });

  it("outra aba segura o bloqueio além do prazo: desligar falha (busy) e o logout fica bloqueado", async () => {
    vi.useFakeTimers();
    const put = deferred<{ keyId: string }>();
    subscribeImpl = () => put.promise;
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tabA = await loadTab();
    void tabA.syncPushOnStart("k1", "u1");
    await vi.advanceTimersByTimeAsync(0); // o PUT da aba A está em voo, segurando o bloqueio
    const tabB = await loadTab();
    const disable = tabB.disablePush(actor());
    await vi.advanceTimersByTimeAsync(tabB.DISABLE_LOCK_TIMEOUT_MS + 1);
    const r = await disable;
    expect(r).toEqual({ status: "failed", reason: "busy" });
    expect(canLogoutAfter(r)).toBe(false);
    expect(log.some((l) => l.startsWith("DELETE"))).toBe(false);
    put.resolve({ keyId: "k1" });
  });
});

describe("marca de desligado precisa ser durável", () => {
  it("cota cheia (setItem lança, getItem funciona) + DELETE ok + cancelamento local falha: failed/storage e logout bloqueado", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(false)), storage: "setThrows" });
    const r = await (await loadTab()).disablePush(actor());
    expect(log).toEqual(["DELETE https://push.example/1"]);
    expect(r).toEqual({ status: "failed", reason: "storage" });
    expect(canLogoutAfter(r)).toBe(false);
  });

  it("gravação some em silêncio (getItem não devolve a marca): também failed/storage", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(false)), storage: "setNoop" });
    const r = await (await loadTab()).disablePush(actor());
    expect(r).toEqual({ status: "failed", reason: "storage" });
    expect(canLogoutAfter(r)).toBe(false);
  });

  it("sem marca durável mas com a inscrição cancelada no navegador: revogação é final (logout permitido)", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(true)), storage: "setThrows" });
    const r = await (await loadTab()).disablePush(actor());
    expect(r).toEqual({ status: "revoked" });
    expect(canLogoutAfter(r)).toBe(true);
  });
});

describe("identificador da inscrição não é guardado em claro", () => {
  it("guarda só o resumo SHA-256 do endpoint, nunca a URL", async () => {
    const { store } = setupBrowser({ sub: makeSub("https://push.example/segredo-123", () => Promise.resolve(false)) });
    store.set("fa.push.serverRevokedEndpoint", "https://push.example/segredo-123"); // chave antiga com URL bruta
    const tab = await loadTab();
    await tab.disablePush(actor());
    const values = [...store.values()].join("|");
    expect(values).not.toContain("push.example");
    expect(store.has("fa.push.serverRevokedEndpoint")).toBe(false);
    expect(store.get("fa.push.serverRevokedDigest")).toBe(await tab.endpointDigest("https://push.example/segredo-123"));
    expect(store.get("fa.push.serverRevokedDigest")).toMatch(/^[0-9a-f]{64}$/);
    expect(await tab.isServerRevoked("https://push.example/segredo-123")).toBe(true);
    expect(await tab.isServerRevoked("https://push.example/outra")).toBe(false);
  });
});

describe("ativação pendente + armazenamento sem gravação (cenário combinado das revisões)", () => {
  async function scenario(storage: "setThrows" | "setNoop" | "allThrow", browserCancels: boolean) {
    const permission = deferred<NotificationPermission>();
    const env = setupBrowser({
      sub: makeSub("https://push.example/1", () => Promise.resolve(browserCancels)),
      storage,
      requestPermission: () => permission.promise,
    });
    const tabA = await loadTab();
    const enable = tabA.enablePush("u1"); // aba A: permissão pendente
    await tick();
    const tabB = await loadTab();
    const r = await tabB.disablePush(actor()); // aba B: logout (DELETE + cancelamento local)
    permission.resolve("granted");
    const e = await enable;
    return { r, e, env };
  }

  it("gravação recusada (cota), leitura ok, cancelamento local OK: ativação antiga é descartada; logout permitido", async () => {
    const { r, e } = await scenario("setThrows", true);
    expect(r).toEqual({ status: "revoked" });
    expect(canLogoutAfter(r)).toBe(true);
    expect(e).toEqual({ ok: false, reason: "cancelled" }); // marcador de revogação, sem depender do localStorage
    expect(log).toEqual(["DELETE https://push.example/1"]); // nenhum POST depois do DELETE
  });

  it("gravação recusada (cota), leitura ok, cancelamento local falha: logout bloqueado e nenhum POST", async () => {
    const { r, e } = await scenario("setThrows", false);
    expect(r).toEqual({ status: "failed", reason: "storage" });
    expect(canLogoutAfter(r)).toBe(false);
    expect(e).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });

  it("gravação que some em silêncio + cancelamento local OK: ativação descartada, sem POST", async () => {
    const { r, e } = await scenario("setNoop", true);
    expect(canLogoutAfter(r)).toBe(true);
    expect(e).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });

  it("leitura e gravação indisponíveis (sequência ilegível/NaN): a ativação nem começa; logout só se o navegador cancelou", async () => {
    const ok = await scenario("allThrow", true);
    expect(ok.e).toEqual({ ok: false, reason: "storage" }); // falha segura com sequência ilegível
    expect(canLogoutAfter(ok.r)).toBe(true);
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });

  it("leitura e gravação indisponíveis + cancelamento local falha: logout bloqueado e nenhum POST", async () => {
    const { r, e } = await scenario("allThrow", false);
    expect(e).toEqual({ ok: false, reason: "storage" });
    expect(canLogoutAfter(r)).toBe(false);
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });

  it("sem como marcar a revogação (Web Locks sem query) e armazenamento sem gravação: ativar fica indisponível", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1"), storage: "setThrows", noLockQuery: true });
    const tab = await loadTab();
    expect(await tab.enablePush("u1")).toEqual({ ok: false, reason: "needs-update" });
    expect(log).toEqual([]);
  });
});

describe("logout com ativação pendente e aba de logout fechada (marcador perdido)", () => {
  async function setup(browserCancels: boolean) {
    const permission = deferred<NotificationPermission>();
    const env = setupBrowser({
      sub: makeSub("https://push.example/1", () => Promise.resolve(browserCancels)),
      storage: "setThrows", // sequência e marca não duráveis
      requestPermission: () => permission.promise,
    });
    const tabA = await loadTab();
    const enable = tabA.enablePush("u1"); // aba A: permissão pendente
    await tick();
    const tabB = await loadTab();
    const logoutCalls: string[] = [];
    const logout = async () => {
      logoutCalls.push("logout");
      sessionUser = null;
    };
    return { env, permission, enable, tabA, tabB, logout, logoutCalls };
  }

  it("A pendente + armazenamento sem escrita + B DELETE e cancelamento local + B fecha antes de A: nenhum POST", async () => {
    const t = await setup(true);
    const res = await t.tabB.disablePushAndLogout(t.logout, actor()); // A confirma o cancelamento (ack) e só então B sai
    expect(res).toEqual({ revocation: { status: "revoked" }, loggedOut: true });
    expect(t.logoutCalls).toEqual(["logout"]);
    t.env.locks?.dropHeld("fa.push.revoked:"); // aba B fechou: o marcador em memória some
    t.permission.resolve("granted"); // só agora a permissão de A chega
    expect(await t.enable).toEqual({ ok: false, reason: "cancelled" }); // cancelada na memória da aba A
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });

  it("mesmo cenário com o cancelamento local falhando: logout não é feito e A continua sem POST", async () => {
    const t = await setup(false);
    const res = await t.tabB.disablePushAndLogout(t.logout, actor());
    expect(res.loggedOut).toBe(false);
    expect(canLogoutAfter(res.revocation)).toBe(false); // revogação só no servidor sem marca durável
    expect(t.logoutCalls).toEqual([]);
    t.env.locks?.dropHeld("fa.push.revoked:");
    t.permission.resolve("granted");
    expect(await t.enable).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });

  it("A não confirma o cancelamento a tempo (aba congelada): logout bloqueado (failed/pending), sem chamar logout", async () => {
    vi.useFakeTimers();
    const permission = deferred<NotificationPermission>();
    const env = setupBrowser({
      sub: makeSub("https://push.example/1", () => Promise.resolve(true)),
      storage: "setThrows",
      requestPermission: () => permission.promise,
    });
    const tabA = await loadTab();
    void tabA.enablePush("u1");
    await vi.advanceTimersByTimeAsync(0);
    env.bus.deafenLast(); // a aba A não responde
    const tabB = await loadTab();
    const logoutCalls: string[] = [];
    const resP = tabB.disablePushAndLogout(async () => void logoutCalls.push("logout"), actor());
    await vi.advanceTimersByTimeAsync(tabB.INTENT_ACK_TIMEOUT_MS + 1);
    const res = await resP;
    expect(res).toEqual({ revocation: { status: "failed", reason: "pending" }, loggedOut: false });
    expect(logoutCalls).toEqual([]);
    expect(log.some((l) => l.startsWith("PUT"))).toBe(false);
  });

  it("ativação que começa durante a janela do logout espera o bloqueio e desiste (sessão encerrada), sem POST", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1"), storage: "setThrows" });
    const tabB = await loadTab();
    const logoutDone = deferred<void>();
    let tabC: Awaited<ReturnType<typeof loadTab>> | null = null;
    let enableC: Promise<unknown> | null = null;
    const res = tabB.disablePushAndLogout(async () => {
      // Enquanto o logout está em andamento, outra aba começa a ativar.
      tabC = await loadTab();
      enableC = tabC.enablePush("u1");
      await tick();
      sessionUser = null;
      await logoutDone.promise;
    }, actor());
    await tick();
    await tick();
    logoutDone.resolve();
    expect((await res).loggedOut).toBe(true);
    expect(await enableC).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });

  it("sem BroadcastChannel: ativar fica indisponível (needs-update), sem gravar", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1"), noBroadcast: true });
    const tab = await loadTab();
    expect(await tab.enablePush("u1")).toEqual({ ok: false, reason: "needs-update" });
    expect(log).toEqual([]);
  });
});

describe("ativação vinculada à conta que a iniciou (troca de sessão)", () => {
  it("A inicia ativação durante o logout, logout termina, B entra antes da permissão: cancelada, sem inscrição nem POST como B", async () => {
    const permission = deferred<NotificationPermission>();
    const env = setupBrowser({
      sub: makeSub("https://push.example/a", () => Promise.resolve(true)),
      storage: "setThrows", // sem armazenamento gravável: sequência e marca não ajudam
      requestPermission: () => permission.promise,
    });
    sessionUser = "uA";
    const tabB = await loadTab(); // aba onde A sai
    let tabC: Awaited<ReturnType<typeof loadTab>> | null = null;
    let enableC: Promise<unknown> | null = null;
    const res = tabB.disablePushAndLogout(async () => {
      // Durante o logout, outra aba (que ainda mostra A) começa a ativar.
      tabC = await loadTab();
      enableC = tabC.enablePush("uA");
      await tick();
      sessionUser = null; // logout de A concluído no servidor
    }, actor());
    expect((await res).loggedOut).toBe(true);
    sessionUser = "uB"; // outra conta entra antes de a permissão ser concedida
    permission.resolve("granted");
    expect(await enableC).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual(["DELETE https://push.example/a"]); // nenhum POST como B
    expect(env.created.count).toBe(0); // nenhuma inscrição nova no navegador
  });

  it("mesma troca de conta sem logout coordenado (sessão A substituída por B em outra aba): cancelada antes do POST", async () => {
    const permission = deferred<NotificationPermission>();
    const env = setupBrowser({ sub: null, requestPermission: () => permission.promise });
    sessionUser = "uA";
    const tab = await loadTab();
    const enable = tab.enablePush("uA");
    await tick();
    sessionUser = "uB";
    permission.resolve("granted");
    expect(await enable).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual([]);
    expect(env.created.count).toBe(0);
  });

  it("sessão ausente, consulta de sessão com erro ou usuário desconhecido no toque: não ativa", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    sessionUser = null;
    expect(await tab.enablePush("u1")).toEqual({ ok: false, reason: "cancelled" });
    sessionUser = "u1";
    meMode = "fail";
    expect(await tab.enablePush("u1")).toEqual({ ok: false, reason: "cancelled" });
    meMode = "normal";
    expect(await tab.enablePush(null)).toEqual({ ok: false, reason: "cancelled" });
    expect(await tab.enablePush(undefined)).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual([]);
  });

  it("mesma conta do toque: ativa normalmente", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    expect(await tab.enablePush("u1")).toEqual({ ok: true });
    expect(log).toEqual(["PUT-start https://push.example/1", "PUT-done https://push.example/1 as u1"]);
  });

  it("o pedido de permissão sai de forma síncrona, ainda dentro do gesto (antes de qualquer espera)", async () => {
    const env = setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    const p = tab.enablePush("u1"); // sem await
    expect(env.permissionRequests.count).toBe(1); // já pedido antes de devolver o controle
    await p;
  });

  it("sem Web Locks nem BroadcastChannel e sem armazenamento gravável: não pede permissão nem grava", async () => {
    const env = setupBrowser({ sub: makeSub("https://push.example/1"), withLocks: false, noBroadcast: true, storage: "setThrows" });
    const tab = await loadTab();
    expect(await tab.enablePush("u1")).toEqual({ ok: false, reason: "needs-update" });
    expect(env.permissionRequests.count).toBe(0);
    expect(log).toEqual([]);
  });
});

describe("contrato expectedUserId no POST de inscrição", () => {
  it("ativação manual envia a conta capturada no toque", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    expect(await tab.enablePush("u1")).toEqual({ ok: true });
    expect(putBodies).toEqual([expect.objectContaining({ endpoint: "https://push.example/1", expectedUserId: "u1" })]);
  });

  it("cookie troca de A para B entre a última consulta (GET /me) e o POST: 409, sem registro em B e inscrição local nova cancelada", async () => {
    const env = setupBrowser({ sub: null }); // sem inscrição: a ativação cria uma nova no navegador
    sessionUser = "uA";
    switchAfterMe = { call: 2, to: "uB" }; // 2ª consulta (a imediatamente antes do POST) responde A; logo depois B entra
    const tab = await loadTab();
    expect(await tab.enablePush("uA")).toEqual({ ok: false, reason: "cancelled" });
    expect(putBodies).toEqual([expect.objectContaining({ expectedUserId: "uA" })]); // nunca reenviado com outra conta
    expect(log).toEqual(["PUT-rejected https://push.example/novo"]); // backend recusou, nada gravado em B
    expect(log.some((l) => l.startsWith("PUT-done"))).toBe(false);
    expect(env.created.count).toBe(1);
    expect(env.created.unsubscribed).toBe(1); // inscrição local recém-criada foi cancelada
  });

  it("mesma troca com inscrição local já existente: 409, sem gravar e sem cancelar a inscrição que já existia", async () => {
    const existingUnsub = { count: 0 };
    setupBrowser({
      sub: makeSub("https://push.example/1", () => {
        existingUnsub.count++;
        return Promise.resolve(true);
      }),
    });
    sessionUser = "uA";
    switchAfterMe = { call: 2, to: "uB" };
    const tab = await loadTab();
    expect(await tab.enablePush("uA")).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual(["PUT-rejected https://push.example/1"]);
    expect(existingUnsub.count).toBe(0);
  });

  it("409 com outro code não é tratado como troca de sessão: o erro segue normalmente", async () => {
    const env = setupBrowser({ sub: null });
    const { ApiError } = (await import("./api")) as unknown as { ApiError: new (m: string, s: number, c?: string | null) => Error };
    subscribeImpl = () => Promise.reject(new ApiError("Conflito de outra regra.", 409, "other_conflict"));
    const tab = await loadTab();
    const r = await tab.enablePush("u1");
    expect(r).toEqual({ ok: false, reason: "error", message: "Conflito de outra regra." }); // não vira "cancelled"
    expect(env.created.unsubscribed).toBe(0); // nada é desfeito como se fosse sessão trocada
    // 409 sem code também não é mascarado
    subscribeImpl = () => Promise.reject(new ApiError("Conflito sem código.", 409, null));
    expect(await tab.enablePush("u1")).toEqual({ ok: false, reason: "error", message: "Conflito sem código." });
  });

  it("sincronização: 409 com outro code propaga o erro (não devolve cancelled)", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const { ApiError } = (await import("./api")) as unknown as { ApiError: new (m: string, s: number, c?: string | null) => Error };
    subscribeImpl = () => Promise.reject(new ApiError("Conflito de outra regra.", 409, "other_conflict"));
    const tab = await loadTab();
    await expect(tab.syncPushOnStart("k1", "u1")).rejects.toThrow("Conflito de outra regra.");
  });

  it("409 session_changed continua virando cancelamento (sincronização)", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    sessionUser = "uB";
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", "uA")).toBe("cancelled");
  });

  it("sincronização automática envia a conta capturada no início do app", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", "u1")).toBe("synced");
    expect(putBodies).toEqual([expect.objectContaining({ expectedUserId: "u1" })]);
  });

  it("sincronização sem identidade capturada não faz POST", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", null)).toBe("skipped");
    expect(await tab.syncPushOnStart("k1", undefined)).toBe("skipped");
    expect(putBodies).toEqual([]);
    expect(log).toEqual([]);
  });

  it("sincronização capturada como A com cookie já em B: 409 vira cancelada, sem gravar em B nem repetir", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    sessionUser = "uB";
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", "uA")).toBe("cancelled");
    expect(putBodies).toEqual([expect.objectContaining({ expectedUserId: "uA" })]);
    expect(log).toEqual(["PUT-rejected https://push.example/1"]);
  });

  it("troca de chave iniciada por A com cookie já de B: DELETE recusado, nada removido, sem POST e inscrição local mantida", async () => {
    const oldUnsub = { calls: 0 };
    const env = setupBrowser({ sub: makeSub("https://push.example/1", async () => (oldUnsub.calls++, true)) });
    env.store.set("fa.push.keyId", "k-antiga"); // servidor trocou a chave
    sessionUser = "uB"; // cookie já é de outra conta
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", "uA")).toBe("cancelled");
    expect(deleteBodies).toEqual([{ endpoint: "https://push.example/1", expectedUserId: "uA" }]);
    expect(log).toEqual(["DELETE-rejected https://push.example/1"]); // remoto intacto
    expect(putBodies).toEqual([]); // nenhum POST
    expect(oldUnsub.calls).toBe(0); // inscrição local (de B) não foi cancelada
    expect(env.created.count).toBe(0); // nenhuma inscrição nova
    expect(env.store.get("fa.push.keyId")).toBe("k-antiga");
  });

  it("troca de chave: DELETE aceito para A, cookie vira B antes do POST: 409 cancela só a inscrição nova", async () => {
    const env = setupBrowser({ sub: makeSub("https://push.example/1") });
    env.store.set("fa.push.keyId", "k-antiga");
    sessionUser = "uA";
    unsubscribeImpl = async () => {
      sessionUser = "uB"; // outra aba entra logo depois do DELETE confirmado
    };
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", "uA")).toBe("cancelled");
    expect(deleteBodies).toEqual([{ endpoint: "https://push.example/1", expectedUserId: "uA" }]);
    expect(log).toEqual(["DELETE https://push.example/1", "PUT-rejected https://push.example/novo"]);
    expect(env.created.unsubscribed).toBe(1);
  });

  it("troca de chave com a mesma conta: grava a inscrição nova com a conta original", async () => {
    const env = setupBrowser({ sub: makeSub("https://push.example/1") });
    env.store.set("fa.push.keyId", "k-antiga");
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", "u1")).toBe("resubscribed");
    expect(deleteBodies).toEqual([{ endpoint: "https://push.example/1", expectedUserId: "u1" }]);
    expect(putBodies).toEqual([expect.objectContaining({ endpoint: "https://push.example/novo", expectedUserId: "u1" })]);
  });
});

describe("contrato expectedUserId no DELETE e no logout", () => {
  it("desligar manual por A com cookie de B: failed/session, nada removido no servidor nem no navegador, logout bloqueado", async () => {
    const oldUnsub = { calls: 0 };
    const env = setupBrowser({ sub: makeSub("https://push.example/1", async () => (oldUnsub.calls++, true)) });
    env.store.set("fa.push.keyId", "k1");
    sessionUser = "uB";
    const tab = await loadTab();
    const r = await tab.disablePush("uA");
    expect(r).toEqual({ status: "failed", reason: "session" });
    expect(canLogoutAfter(r)).toBe(false);
    expect(log).toEqual(["DELETE-rejected https://push.example/1"]);
    expect(oldUnsub.calls).toBe(0);
    expect(env.store.get("fa.push.keyId")).toBe("k1");
    expect(env.store.get("fa.push.optOut") ?? null).toBeNull(); // marca de desligado desfeita
    expect(env.store.get("fa.push.serverRevokedDigest") ?? null).toBeNull();
  });

  it("desligar sem conta capturada não chama a API", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    expect(await tab.disablePush(undefined)).toEqual({ status: "failed", reason: "session" });
    expect(deleteBodies).toEqual([]);
  });

  it("desligar com a mesma conta: DELETE com expectedUserId e inscrição local cancelada depois", async () => {
    const order: string[] = [];
    unsubscribeImpl = async () => void order.push("server");
    setupBrowser({ sub: makeSub("https://push.example/1", async () => (order.push("local"), true)) });
    const tab = await loadTab();
    expect(await tab.disablePush("u1")).toEqual({ status: "revoked" });
    expect(deleteBodies).toEqual([{ endpoint: "https://push.example/1", expectedUserId: "u1" }]);
    expect(order).toEqual(["server", "local"]);
  });

  it("logout de A com cookie de B: nada removido, logout não chamado, sessão mantida", async () => {
    const oldUnsub = { calls: 0 };
    setupBrowser({ sub: makeSub("https://push.example/1", async () => (oldUnsub.calls++, true)) });
    sessionUser = "uB";
    const tab = await loadTab();
    const calls: string[] = [];
    const res = await tab.disablePushAndLogout(async () => void calls.push("logout"), "uA");
    expect(res).toEqual({ revocation: { status: "failed", reason: "session" }, loggedOut: false, sessionChanged: true });
    expect(calls).toEqual([]);
    expect(oldUnsub.calls).toBe(0);
    expect(sessionUser).toBe("uB");
  });

  it("cookie muda entre o DELETE e o logout: 409 session_changed do logout não é tratado como saída", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    const { ApiError } = await import("./api");
    const res = await tab.disablePushAndLogout(async () => {
      throw new ApiError("Sessão alterada", 409, "session_changed", null);
    }, "u1");
    expect(res.loggedOut).toBe(false);
    expect(res.sessionChanged).toBe(true);
  });

  it("logout: outro 409 (sem session_changed) é repassado como erro", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    const { ApiError } = await import("./api");
    await expect(
      tab.disablePushAndLogout(async () => {
        throw new ApiError("Conflito", 409, null, null);
      }, "u1"),
    ).rejects.toThrow("Conflito");
  });

  it("logout com a mesma conta: revoga e sai", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    const calls: string[] = [];
    const res = await tab.disablePushAndLogout(async () => void calls.push("logout"), "u1");
    expect(res).toEqual({ revocation: { status: "revoked" }, loggedOut: true });
    expect(calls).toEqual(["logout"]);
    expect(deleteBodies).toEqual([{ endpoint: "https://push.example/1", expectedUserId: "u1" }]);
  });
});

describe("registerServiceWorker", () => {
  it("falha não vira null nem fica em cache: a próxima chamada tenta de novo", async () => {
    const env = setupBrowser({ sub: null, registerFails: true });
    const push = await loadTab();
    await expect(push.registerServiceWorker()).rejects.toThrow("registro falhou");
    await expect(push.registerServiceWorker()).rejects.toThrow("registro falhou");
    expect(env.registerCalls()).toBe(2);
  });
});

describe("revogação é final contra sincronizações (mesma aba e entre abas)", () => {
  it("duas sincronizações na mesma aba com envios em voo: o DELETE vem depois de todos e nada é regravado", async () => {
    const puts: Array<{ resolve: (v: { keyId: string }) => void }> = [];
    subscribeImpl = () => {
      const d = deferred<{ keyId: string }>();
      puts.push(d);
      return d.promise;
    };
    const failingUnsubscribe = () => Promise.resolve(false); // navegador não cancela
    setupBrowser({ sub: makeSub("https://push.example/1", failingUnsubscribe) });
    const tab = await loadTab();
    const s1 = tab.syncPushOnStart("k1", "u1");
    const s2 = tab.syncPushOnStart("k1", "u1"); // remontagem do sininho: segunda sincronização na mesma aba
    await tick();
    expect(puts.length).toBe(1); // a segunda espera o bloqueio (serializada)
    const disable = tab.disablePush(actor());
    await tick();
    expect(log.some((l) => l.startsWith("DELETE"))).toBe(false); // esperando o PUT em voo
    puts[0].resolve({ keyId: "k1" });
    const r = await disable;
    expect(await s1).toBe("synced");
    expect(await s2).toBe("cancelled"); // pegou o bloqueio depois do desligar: não grava
    expect(puts.length).toBe(1);
    expect(r).toEqual({ status: "partial", serverRevoked: true, browserRevoked: false });
    expect(canLogoutAfter(r)).toBe(true);
    expect(lastServerWrite()).toBe("DELETE https://push.example/1");
    // Depois disso, nenhuma sincronização (desta aba ou de outra) recria a inscrição.
    expect(await tab.syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(await (await loadTab()).syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(lastServerWrite()).toBe("DELETE https://push.example/1");
  });

  it("entre abas: envio já em voo na aba A, desligar na aba B com falha no cancelamento local", async () => {
    const put = deferred<{ keyId: string }>();
    subscribeImpl = () => put.promise;
    setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.reject(new Error("cancelamento falhou"))) });
    const tabA = await loadTab();
    const syncA = tabA.syncPushOnStart("k1", "u1");
    await tick();
    expect(log).toEqual(["PUT-start https://push.example/1"]); // aba A gravando
    const tabB = await loadTab();
    const disableB = tabB.disablePush(actor());
    await tick();
    expect(log).toEqual(["PUT-start https://push.example/1"]); // aba B espera, não revoga antes
    put.resolve({ keyId: "k1" });
    expect(await syncA).toBe("synced");
    const r = await disableB;
    expect(r).toEqual({ status: "partial", serverRevoked: true, browserRevoked: false });
    expect(log).toEqual(["PUT-start https://push.example/1", "PUT-done https://push.example/1 as u1", "DELETE https://push.example/1"]);
    // A aba A continua aberta e tenta sincronizar de novo (ex.: remontagem): não grava mais.
    expect(await tabA.syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(lastServerWrite()).toBe("DELETE https://push.example/1");
  });

  it("entre abas: sincronização da aba A esperando o bloqueio é cancelada pelo desligar da aba B", async () => {
    const put = deferred<{ keyId: string }>();
    let first = true;
    subscribeImpl = () => (first ? ((first = false), put.promise) : Promise.resolve({ keyId: "k1" }));
    setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(false)) });
    const tabA = await loadTab();
    const tabC = await loadTab();
    const syncA = tabA.syncPushOnStart("k1", "u1"); // segura o bloqueio com PUT em voo
    await tick();
    const syncC = tabC.syncPushOnStart("k1", "u1"); // aba C na fila do bloqueio
    const tabB = await loadTab();
    const disableB = tabB.disablePush(actor()); // marca desligado e entra na fila depois de C
    put.resolve({ keyId: "k1" });
    expect(await syncA).toBe("synced");
    expect(await syncC).toBe("cancelled"); // conferiu a marca dentro do bloqueio
    await disableB;
    expect(log.filter((l) => l.startsWith("PUT-start")).length).toBe(1);
    expect(lastServerWrite()).toBe("DELETE https://push.example/1");
  });

  it("religar numa aba enquanto outra desliga: o desligar regrava a marca e fica por último", async () => {
    const put = deferred<{ keyId: string }>();
    subscribeImpl = () => put.promise;
    const { store } = setupBrowser({ sub: makeSub("https://push.example/1") });
    const tabA = await loadTab();
    const tabB = await loadTab();
    const enable = tabA.enablePush("u1");
    await tick(); // aba A segura o bloqueio com o PUT do ligar em voo
    const disable = tabB.disablePush(actor()); // marca desligado e espera
    put.resolve({ keyId: "k1" }); // o ligar termina e remove a marca...
    expect(await enable).toEqual({ ok: true });
    expect(await disable).toEqual({ status: "revoked" });
    expect(store.get("fa.push.optOut")).toBe("1");
    expect(lastServerWrite()).toBe("DELETE https://push.example/1");
  });

  it("entre abas: ativação esperando a permissão quando outra aba desliga é descartada (nenhum PUT depois do DELETE)", async () => {
    const permission = deferred<NotificationPermission>();
    const { store } = setupBrowser({
      sub: makeSub("https://push.example/1", () => Promise.resolve(false)),
      requestPermission: () => permission.promise,
    });
    const tabA = await loadTab();
    const enable = tabA.enablePush("u1"); // aguardando o usuário responder à permissão
    await tick();
    const tabB = await loadTab();
    const r = await tabB.disablePush(actor()); // logout em outra aba
    expect(r).toEqual({ status: "partial", serverRevoked: true, browserRevoked: false });
    permission.resolve("granted"); // só agora a permissão chega
    expect(await enable).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual(["DELETE https://push.example/1"]);
    expect(store.get("fa.push.optOut")).toBe("1");
    expect(await tabA.syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(lastServerWrite()).toBe("DELETE https://push.example/1");
  });

  it("mesma aba: ativação esperando a permissão é descartada se o desligar acontecer antes", async () => {
    const permission = deferred<NotificationPermission>();
    setupBrowser({ sub: makeSub("https://push.example/1"), requestPermission: () => permission.promise });
    const tab = await loadTab();
    const enable = tab.enablePush("u1");
    await tick();
    expect(await tab.disablePush(actor())).toEqual({ status: "revoked" });
    permission.resolve("granted");
    expect(await enable).toEqual({ ok: false, reason: "cancelled" });
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });

  it("religar explicitamente remove a marca e a sincronização volta a funcionar", async () => {
    const { store } = setupBrowser({ sub: makeSub("https://push.example/1") });
    store.set("fa.push.optOut", "1");
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(await tab.enablePush("u1")).toEqual({ ok: true });
    expect(store.has("fa.push.optOut")).toBe(false);
    log.length = 0;
    expect(await tab.syncPushOnStart("k1", "u1")).toBe("synced");
    expect(log).toEqual(["PUT-start https://push.example/1", "PUT-done https://push.example/1 as u1"]);
  });

  it("sem Web Locks: ativar numa aba e desligar noutra com falha no cancelamento local nunca gera POST depois do DELETE", async () => {
    const { store } = setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(false)), withLocks: false });
    const tabA = await loadTab();
    const tabB = await loadTab();
    const enable = tabA.enablePush("u1"); // concorrente com o desligar da aba B
    const disable = tabB.disablePush(actor());
    expect(await enable).toEqual({ ok: false, reason: "needs-update" });
    const r = await disable;
    expect(r).toEqual({ status: "partial", serverRevoked: true, browserRevoked: false });
    expect(log).toEqual(["DELETE https://push.example/1"]); // nenhum PUT, nem antes nem depois
    expect(store.get("fa.push.optOut")).toBe("1");
    expect(await tabA.enablePush("u1")).toEqual({ ok: false, reason: "needs-update" }); // tentar de novo também não grava
    expect(await tabA.syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(lastServerWrite()).toBe("DELETE https://push.example/1");
    expect(tabA.canCoordinatePush()).toBe(false);
  });

  it("remoção confirmada no servidor com inscrição local restante é exposta para a tela (mostrar Ativar)", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(false)) });
    const tab = await loadTab();
    expect(await tab.isServerRevoked("https://push.example/1")).toBe(false);
    await tab.disablePush(actor()); // servidor revoga, navegador mantém a inscrição
    expect(await tab.getSubscription()).not.toBeNull();
    expect(tab.isPushOptedOut()).toBe(true);
    expect(await tab.isServerRevoked("https://push.example/1")).toBe(true);
    expect(tab.canCoordinatePush()).toBe(true);
  });

  it("DELETE sem confirmação: nada muda (marca desfeita, inscrição mantida, tela segue em Desativar)", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(false)) });
    unsubscribeImpl = () => Promise.reject(new Error("rede"));
    const tab = await loadTab();
    expect(await tab.disablePush(actor())).toEqual({ status: "failed", reason: "server" });
    expect(tab.isPushOptedOut()).toBe(false); // estado anterior restaurado; o usuário tenta de novo
    expect(await tab.isServerRevoked("https://push.example/1")).toBe(false);
  });

  it("sem Web Locks (fallback seguro): a sincronização automática não grava; desligar continua funcionando", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1"), withLocks: false });
    const tab = await loadTab();
    expect(await tab.syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(log).toEqual([]);
    expect(await tab.disablePush(actor())).toEqual({ status: "revoked" });
    expect(log).toEqual(["DELETE https://push.example/1"]);
  });
});

describe("só HTTP 204 do DELETE libera o cancelamento local e o logout", () => {
  async function apiError(status: number, code: string | null = null) {
    const { ApiError } = (await import("./api")) as unknown as { ApiError: new (m: string, s: number, c?: string | null) => Error };
    return new ApiError(`HTTP ${status}`, status, code);
  }
  const cases: Array<[string, number, string | null]> = [
    ["404", 404, null],
    ["500", 500, null],
    ["503", 503, null],
    ["outro 409", 409, "other_conflict"],
    ["409 sem code", 409, null],
  ];

  for (const [label, status, code] of cases) {
    it(`desligar com DELETE ${label}: failed/server, sem cancelamento local nem mudança de estado`, async () => {
      const localUnsub = { calls: 0 };
      const env = setupBrowser({ sub: makeSub("https://push.example/1", async () => (localUnsub.calls++, true)) });
      const err = await apiError(status, code);
      unsubscribeImpl = () => Promise.reject(err);
      const tab = await loadTab();
      const r = await tab.disablePush("u1");
      expect(r).toEqual({ status: "failed", reason: "server" });
      expect(canLogoutAfter(r)).toBe(false);
      expect(localUnsub.calls).toBe(0);
      expect(env.store.get("fa.push.keyId")).toBe("k1");
      expect(env.store.get("fa.push.optOut") ?? null).toBeNull();
      expect(env.store.get("fa.push.serverRevokedDigest") ?? null).toBeNull();
      expect([...env.store.keys()].some((k) => k.startsWith("fa.push.revokeSeq"))).toBe(false);
    });

    it(`logout com DELETE ${label}: logout não é chamado e a inscrição local fica`, async () => {
      const localUnsub = { calls: 0 };
      setupBrowser({ sub: makeSub("https://push.example/1", async () => (localUnsub.calls++, true)) });
      const err = await apiError(status, code);
      unsubscribeImpl = () => Promise.reject(err);
      const tab = await loadTab();
      const calls: string[] = [];
      const res = await tab.disablePushAndLogout(async () => void calls.push("logout"), "u1");
      expect(res).toEqual({ revocation: { status: "failed", reason: "server" }, loggedOut: false });
      expect(calls).toEqual([]);
      expect(localUnsub.calls).toBe(0);
    });
  }

  it("depois da falha, nova tentativa com 204 revoga e sai normalmente", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    unsubscribeImpl = () => Promise.reject(new Error("rede"));
    const tab = await loadTab();
    expect((await tab.disablePushAndLogout(async () => undefined, "u1")).loggedOut).toBe(false);
    unsubscribeImpl = () => Promise.resolve();
    const calls: string[] = [];
    const res = await tab.disablePushAndLogout(async () => void calls.push("logout"), "u1");
    expect(res).toEqual({ revocation: { status: "revoked" }, loggedOut: true });
    expect(calls).toEqual(["logout"]);
  });
});

describe("revogação recusada não afeta ativações de outra conta (entre abas)", () => {
  it("aba antiga de A com cookie B: DELETE 409 e a ativação pendente de B continua e grava", async () => {
    const permission = deferred<NotificationPermission>();
    const env = setupBrowser({ sub: makeSub("https://push.example/1"), requestPermission: () => permission.promise });
    sessionUser = "uB";
    const tabB = await loadTab();
    const enableB = tabB.enablePush("uB"); // aba B: permissão pendente
    await tick();
    const tabA = await loadTab(); // aba antiga que ainda mostra A
    const r = await tabA.disablePush("uA");
    expect(r).toEqual({ status: "failed", reason: "session" });
    expect(env.locks?.heldNames().some((n) => n.startsWith("fa.push.revoked:"))).toBe(false); // nenhum marcador criado
    expect(env.bus.sent().some((m) => (m as { type?: string }).type === "revoke")).toBe(false); // nenhum cancelamento enviado
    permission.resolve("granted");
    expect(await enableB).toEqual({ ok: true });
    expect(log).toEqual(["DELETE-rejected https://push.example/1", "PUT-start https://push.example/1", "PUT-done https://push.example/1 as uB"]);
  });

  it("mesmo com DELETE 204 de A, a ativação pendente de B (outra conta) não é cancelada; a de A é", async () => {
    const permission = deferred<NotificationPermission>();
    const env = setupBrowser({ sub: makeSub("https://push.example/1"), requestPermission: () => permission.promise });
    sessionUser = "uA";
    const tabB = await loadTab();
    const enableB = tabB.enablePush("uB"); // intenção de B (ex.: aba que já mostra B)
    const tabA2 = await loadTab();
    const enableA = tabA2.enablePush("uA"); // intenção de A em outra aba
    await tick();
    const tabA = await loadTab();
    expect(await tabA.disablePush("uA")).toEqual({ status: "revoked" });
    const revokes = env.bus.sent().filter((m) => (m as { type?: string }).type === "revoke") as Array<{ userId: string }>;
    expect(revokes.map((m) => m.userId)).toEqual(["uA"]);
    sessionUser = "uB"; // B entra depois
    permission.resolve("granted");
    expect(await enableA).toEqual({ ok: false, reason: "cancelled" });
    expect(await enableB).toEqual({ ok: true });
    expect(putBodies.map((b) => b.expectedUserId)).toEqual(["uB"]);
  });
});

describe("contrato de sessão no servidor: logout 204 sem Set-Cookie, sessão revogada/expirada = 401", () => {
  async function apiError(status: number) {
    const { ApiError } = (await import("./api")) as unknown as { ApiError: new (m: string, s: number, c?: string | null) => Error };
    return new ApiError(`HTTP ${status}`, status, null);
  }

  it("logout 204 sem apagar cookie: sai normalmente (a sessão some no servidor; nada depende de Set-Cookie)", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    const res = await tab.disablePushAndLogout(async () => void (sessionUser = null), "u1");
    expect(res).toEqual({ revocation: { status: "revoked" }, loggedOut: true });
  });

  it("DELETE 401 (sessão já revogada/expirada): sessionEnded, sem logout, sem cancelamento local e sem mudar estado", async () => {
    const localUnsub = { calls: 0 };
    const env = setupBrowser({ sub: makeSub("https://push.example/1", async () => (localUnsub.calls++, true)) });
    const err = await apiError(401);
    unsubscribeImpl = () => Promise.reject(err);
    const tab = await loadTab();
    const calls: string[] = [];
    const res = await tab.disablePushAndLogout(async () => void calls.push("logout"), "u1");
    expect(res).toEqual({ revocation: { status: "failed", reason: "unauthenticated" }, loggedOut: false, sessionEnded: true });
    expect(calls).toEqual([]);
    expect(localUnsub.calls).toBe(0);
    expect(env.store.get("fa.push.optOut") ?? null).toBeNull();
    expect(env.store.get("fa.push.keyId")).toBe("k1");
  });

  it("desligar com DELETE 401: failed/unauthenticated (logout bloqueado), nada local muda", async () => {
    const localUnsub = { calls: 0 };
    setupBrowser({ sub: makeSub("https://push.example/1", async () => (localUnsub.calls++, true)) });
    const err = await apiError(401);
    unsubscribeImpl = () => Promise.reject(err);
    const tab = await loadTab();
    const r = await tab.disablePush("u1");
    expect(r).toEqual({ status: "failed", reason: "unauthenticated" });
    expect(canLogoutAfter(r)).toBe(false);
    expect(localUnsub.calls).toBe(0);
  });

  it("sem inscrição e logout 401 (sessão revogada em outra aba): tratado como saída já feita", async () => {
    setupBrowser({ sub: null });
    const tab = await loadTab();
    const err = await apiError(401);
    const res = await tab.disablePushAndLogout(async () => {
      throw err;
    }, "u1");
    expect(res).toEqual({ revocation: { status: "none" }, loggedOut: true, sessionEnded: true });
  });

  it("DELETE 204 e logout 401 (sessão revogada entre os dois): saída já feita, push revogado", async () => {
    setupBrowser({ sub: makeSub("https://push.example/1") });
    const tab = await loadTab();
    const err = await apiError(401);
    const res = await tab.disablePushAndLogout(async () => {
      throw err;
    }, "u1");
    expect(res).toEqual({ revocation: { status: "revoked" }, loggedOut: true, sessionEnded: true });
  });

  it("logout 500 continua sendo erro (não vira saída)", async () => {
    setupBrowser({ sub: null });
    const tab = await loadTab();
    const err = await apiError(500);
    await expect(
      tab.disablePushAndLogout(async () => {
        throw err;
      }, "u1"),
    ).rejects.toThrow("HTTP 500");
  });
});

describe("falha antiga não desfaz o desligar confirmado de outra aba (opt-out sob o bloqueio)", () => {
  /**
   * A: desligar com DELETE falhando; o bloqueio é liberado mas A só retoma depois (janela da especificação).
   * B: nesse intervalo, DELETE 204 com falha no cancelamento local (revogação só no servidor) e marca confirmada.
   * A retoma; depois, a sincronização da aba C não pode recriar a inscrição.
   */
  async function scenario(runA: (tab: Awaited<ReturnType<typeof loadTab>>) => Promise<unknown>) {
    const env = setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(false)) });
    let deleteCalls = 0;
    unsubscribeImpl = () => (++deleteCalls === 1 ? Promise.reject(new Error("rede")) : Promise.resolve());
    const tabA = await loadTab();
    const tabB = await loadTab();
    const tabC = await loadTab();
    const gate = env.locks!.gateNextResult(tabA.PUSH_WRITE_LOCK);
    let aDone = false;
    const a = runA(tabA).then((r) => ((aDone = true), r));
    for (let i = 0; i < 5; i++) await tick(); // A fez o DELETE (falhou) e soltou o bloqueio, mas está retida
    expect(aDone).toBe(false);
    expect(env.locks!.heldNames()).not.toContain(tabA.PUSH_WRITE_LOCK);
    const rB = await tabB.disablePush("u1");
    expect(rB).toEqual({ status: "partial", serverRevoked: true, browserRevoked: false });
    expect(env.store.get("fa.push.optOut")).toBe("1");
    gate.release(); // A retoma só agora
    const rA = await a;
    return { env, rA, tabC };
  }

  it("desligar: A tardia não apaga a marca de B e a sincronização de C não recria a inscrição", async () => {
    const { env, rA, tabC } = await scenario((tab) => tab.disablePush("u1"));
    expect(rA).toEqual({ status: "failed", reason: "server" });
    expect(env.store.get("fa.push.optOut")).toBe("1"); // marca confirmada por B intacta
    expect(env.store.has("fa.push.disabling")).toBe(false); // nenhum token pendente sobrou
    expect(await tabC.syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(log.some((l) => l.startsWith("PUT"))).toBe(false);
    expect(lastServerWrite()).toBe("DELETE https://push.example/1");
  });

  it("logout: A tardia (DELETE falhou, sem logout) também não desfaz a marca de B; C não regrava", async () => {
    const calls: string[] = [];
    const { env, rA, tabC } = await scenario((tab) => tab.disablePushAndLogout(async () => void calls.push("logout"), "u1"));
    expect(rA).toEqual({ revocation: { status: "failed", reason: "server" }, loggedOut: false });
    expect(calls).toEqual([]);
    expect(env.store.get("fa.push.optOut")).toBe("1");
    expect(await tabC.syncPushOnStart("k1", "u1")).toBe("skipped");
    expect(log.some((l) => l.startsWith("PUT"))).toBe(false);
  });

  it("desligar que desiste por tempo (busy) não toca a marca confirmada por outra aba", async () => {
    vi.useFakeTimers();
    const env = setupBrowser({ sub: makeSub("https://push.example/1", () => Promise.resolve(false)) });
    env.store.set("fa.push.optOut", "1"); // confirmada antes por outra aba
    const tabA = await loadTab();
    const hold = deferred<void>();
    void env.locks!.request(tabA.PUSH_WRITE_LOCK, {}, () => hold.promise); // outra aba segura o bloqueio
    const pA = tabA.disablePush("u1");
    await vi.advanceTimersByTimeAsync(tabA.DISABLE_LOCK_TIMEOUT_MS + 1);
    expect(await pA).toEqual({ status: "failed", reason: "busy" });
    expect(env.store.get("fa.push.optOut")).toBe("1");
    expect(env.store.has("fa.push.disabling")).toBe(false);
    hold.resolve();
  });

  it("desligar em andamento faz a sincronização na fila desistir, sem alterar a marca confirmada", async () => {
    const put = deferred<{ keyId: string }>();
    let first = true;
    subscribeImpl = () => (first ? ((first = false), put.promise) : Promise.resolve({ keyId: "k1" }));
    const env = setupBrowser({ sub: makeSub("https://push.example/1") });
    unsubscribeImpl = () => Promise.reject(new Error("rede"));
    const tabA = await loadTab();
    const tabC = await loadTab();
    const syncA = tabA.syncPushOnStart("k1", "u1"); // segura o bloqueio com o PUT em voo
    await tick();
    const tabB = await loadTab();
    const disableB = tabB.disablePush("u1"); // desligar pendente
    const syncC = tabC.syncPushOnStart("k1", "u1"); // entra na fila depois
    put.resolve({ keyId: "k1" });
    expect(await syncA).toBe("synced");
    expect(await disableB).toEqual({ status: "failed", reason: "server" });
    expect(await syncC).toBe("skipped"); // havia desligar em andamento quando C começou: desiste sem gravar
    expect(log.filter((l) => l.startsWith("PUT-start")).length).toBe(1);
    expect(env.store.get("fa.push.optOut") ?? null).toBeNull(); // nada confirmado, nada restaurado
    expect(env.store.has("fa.push.disabling")).toBe(false);
    expect(await tabC.syncPushOnStart("k1", "u1")).toBe("synced"); // desligar falhou e terminou: volta a sincronizar
  });
});
