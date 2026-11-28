import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "./api";
import { AUTH_COOKIE_LOCK, AuthBusyError, withAuthCookieLock } from "./authLock";

/** Web Locks simulada: exclusiva por nome, FIFO, comum a todas as "abas"; respeita `signal` antes de começar. */
function makeLocks() {
  const tails = new Map<string, Promise<unknown>>();
  return {
    request(name: string, opts: { signal?: AbortSignal }, fn: () => Promise<unknown>) {
      let started = false;
      let abortReject: ((e: unknown) => void) | null = null;
      const aborted = new Promise<never>((_, reject) => (abortReject = reject));
      opts.signal?.addEventListener("abort", () => {
        if (!started) abortReject?.(new DOMException("Aborted", "AbortError"));
      });
      const run = (tails.get(name) ?? Promise.resolve()).then(async () => {
        if (opts.signal?.aborted) throw new DOMException("Aborted", "AbortError");
        started = true;
        return fn();
      });
      tails.set(name, run.catch(() => undefined));
      return opts.signal ? Promise.race([run, aborted]) : run;
    },
  };
}

function deferred<T>() {
  let resolve!: (v: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}

const tick = () => new Promise((r) => setTimeout(r, 0));

/**
 * Servidor + "pote de cookies" do navegador. Cada resposta aplica o Set-Cookie NO MOMENTO em que chega,
 * como o navegador faz; o teste controla a ordem de chegada.
 */
function makeServer() {
  const cookie = { value: "A" as string | null };
  const revoked = new Set<string | null>();
  const sent: string[] = [];
  const pending = new Map<string, () => void>();
  vi.stubGlobal("fetch", (u: string) => {
    const path = new URL(u, "http://x").pathname;
    sent.push(path);
    const d = deferred<Response>();
    pending.set(path, () => {
      if (path === "/api/auth/logout") {
        // Contrato atual: o servidor revoga a sessão e responde 204 SEM Set-Cookie; o cookie do navegador fica.
        revoked.add(cookie.value);
        d.resolve(new Response(null, { status: 204 }));
      } else {
        cookie.value = "B"; // Set-Cookie: fa_auth=<sessão B>
        d.resolve(new Response(JSON.stringify({ id: "uB" }), { status: 200, headers: { "Content-Type": "application/json" } }));
      }
    });
    return d.promise;
  });
  return { cookie, revoked, sent, respond: (path: string) => pending.get(path)?.() };
}

afterEach(() => vi.unstubAllGlobals());

describe("login e logout serializados entre abas (cookie de sessão)", () => {
  it("logout de A em voo + login de B em outra aba: o login só sai depois da resposta do logout; o cookie final é o de B", async () => {
    vi.stubGlobal("navigator", { locks: makeLocks() });
    const server = makeServer();
    const logoutA = api.logout("uA"); // aba 1
    await tick();
    const loginB = api.login("11999990000", "senha"); // aba 2, enquanto o logout de A não respondeu
    await tick();
    expect(server.sent).toEqual(["/api/auth/logout"]); // login esperando o bloqueio
    server.respond("/api/auth/logout"); // resposta (expira o cookie) chega
    await logoutA;
    await tick();
    expect(server.sent).toEqual(["/api/auth/logout", "/api/auth/login"]);
    server.respond("/api/auth/login");
    expect(await loginB).toMatchObject({ id: "uB" });
    expect(server.cookie.value).toBe("B"); // o logout de A não mexe no cookie: B continua
    expect(server.revoked.has("A")).toBe(true); // só a sessão de A foi revogada no servidor
    expect(server.revoked.has("B")).toBe(false);
  });

  it("sem lock e com a resposta do logout de A chegando depois do login de B: o cookie de B continua (204 sem Set-Cookie)", async () => {
    vi.stubGlobal("navigator", {});
    const server = makeServer();
    // Abas diferentes sem Web Locks: simula chamando o fetch direto, fora da fila desta aba.
    const logoutA = fetch("/api/auth/logout", { method: "POST" });
    const loginB = fetch("/api/auth/login", { method: "POST" });
    server.respond("/api/auth/login"); // B instala o cookie primeiro
    await loginB;
    server.respond("/api/auth/logout"); // a resposta tardia do logout de A chega depois
    await logoutA;
    expect(server.cookie.value).toBe("B");
  });

  it("ordem inversa: login de B primeiro e logout de A em outra aba espera; a resposta tardia do logout não ocorre durante o login", async () => {
    vi.stubGlobal("navigator", { locks: makeLocks() });
    const server = makeServer();
    const loginB = api.login("11999990000", "senha");
    await tick();
    const logoutA = api.logout("uA");
    await tick();
    expect(server.sent).toEqual(["/api/auth/login"]);
    server.respond("/api/auth/login");
    await loginB;
    await tick();
    // O logout só é enviado agora, com o cookie de B: a API responde 409 session_changed sem expirar nada
    // (contrato do backend); aqui basta conferir que ele não correu em paralelo com o login.
    expect(server.sent).toEqual(["/api/auth/login", "/api/auth/logout"]);
    server.respond("/api/auth/logout");
    await logoutA;
  });

  it("cadastro e acesso de admin também esperam o logout em voo", async () => {
    vi.stubGlobal("navigator", { locks: makeLocks() });
    const server = makeServer();
    const logoutA = api.logout("uA");
    await tick();
    const reg = api.register("11999990000", "B", "senha");
    const setup = api.adminSetup("c", "11999990000", "senha");
    const recovery = api.adminRecovery("c", "11999990000", "senha");
    await tick();
    expect(server.sent).toEqual(["/api/auth/logout"]);
    server.respond("/api/auth/logout");
    await logoutA;
    for (const [p, path] of [
      [reg, "/api/auth/register"],
      [setup, "/api/auth/admin-setup"],
      [recovery, "/api/auth/admin-recovery"],
    ] as const) {
      await tick();
      expect(server.sent.at(-1)).toBe(path);
      server.respond(path);
      await p;
    }
  });

  it("bloqueio ocupado além do prazo: desiste com AuthBusyError SEM enviar a requisição", async () => {
    const locks = makeLocks();
    vi.stubGlobal("navigator", { locks });
    const hold = deferred<void>();
    void locks.request(AUTH_COOKIE_LOCK, {}, () => hold.promise); // outra aba segura o bloqueio
    const fn = vi.fn(async () => "enviado");
    await expect(withAuthCookieLock(fn, 20)).rejects.toBeInstanceOf(AuthBusyError);
    expect(fn).not.toHaveBeenCalled();
    hold.resolve();
  });

  it("falha ao pedir o bloqueio: o logout não é enviado", async () => {
    vi.stubGlobal("navigator", { locks: { request: () => Promise.reject(new DOMException("negado", "SecurityError")) } });
    const fetchSpy = vi.fn();
    vi.stubGlobal("fetch", fetchSpy);
    await expect(api.logout("uA")).rejects.toThrow("negado");
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it("sem Web Locks: serializa na própria aba e o logout continua possível", async () => {
    vi.stubGlobal("navigator", {});
    const server = makeServer();
    const logoutA = api.logout("uA");
    const loginB = api.login("11999990000", "senha");
    await tick();
    expect(server.sent).toEqual(["/api/auth/logout"]);
    server.respond("/api/auth/logout");
    await logoutA;
    await tick();
    server.respond("/api/auth/login");
    await loginB;
    expect(server.cookie.value).toBe("B");
  });
});
