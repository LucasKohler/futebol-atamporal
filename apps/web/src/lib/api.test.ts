import { afterEach, describe, expect, it, vi } from "vitest";
import { api, problemMessage } from "./api";

describe("problemMessage", () => {
  it("prefers the business rule detail", () => {
    expect(problemMessage({ title: "Regra de negócio", detail: "Transição de Draft para Completed não é permitida." }, "x")).toBe(
      "Transição de Draft para Completed não é permitida.",
    );
  });

  it("names invalid fields from validation problems", () => {
    const body = { title: "One or more validation errors occurred.", errors: { "$.status": ["could not be converted"], body: ["required"] } };
    expect(problemMessage(body, "x")).toBe("Valor inválido em: status, body.");
  });

  it("falls back when the body is not a problem", () => {
    expect(problemMessage(null, "Erro 500")).toBe("Erro 500");
    expect(problemMessage({ title: "Conflito" }, "x")).toBe("Conflito");
  });
});

describe("api.me", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("consulta a sessão sem cache (vínculo do push à conta precisa da resposta atual)", async () => {
    let init: RequestInit | undefined;
    vi.stubGlobal("fetch", (_url: string, i?: RequestInit) => {
      init = i;
      return Promise.resolve(new Response(JSON.stringify({ id: "u1" }), { status: 200, headers: { "Content-Type": "application/json" } }));
    });
    expect(await api.me()).toMatchObject({ id: "u1" });
    expect(init?.cache).toBe("no-store");
    expect(init?.credentials).toBe("include");
  });

  it("401 vira null (sem sessão)", async () => {
    vi.stubGlobal("fetch", () => Promise.resolve(new Response(null, { status: 401 })));
    expect(await api.me()).toBeNull();
  });
});

describe("contrato expectedUserId no DELETE de inscrição e no logout", () => {
  afterEach(() => vi.unstubAllGlobals());

  function capture(status = 204, body: unknown = null) {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal("fetch", (u: string, init?: RequestInit) => {
      calls.push({ url: u, init });
      return Promise.resolve(
        new Response(body === null ? null : JSON.stringify(body), {
          status,
          headers: { "Content-Type": "application/problem+json" },
        }),
      );
    });
    return calls;
  }

  it("DELETE envia endpoint e expectedUserId", async () => {
    const calls = capture();
    await api.unsubscribePush("https://push.example/1", "u1");
    expect(calls[0].init?.method).toBe("DELETE");
    expect(calls[0].url).toContain("/api/notifications/subscriptions");
    expect(JSON.parse(String(calls[0].init?.body))).toEqual({ endpoint: "https://push.example/1", expectedUserId: "u1" });
  });

  it("DELETE 409 session_changed vira ApiError com code", async () => {
    capture(409, { title: "Sessão alterada", status: 409, detail: "A sessão mudou.", code: "session_changed" });
    await expect(api.unsubscribePush("https://push.example/1", "u1")).rejects.toMatchObject({ status: 409, code: "session_changed" });
  });

  it("DELETE só resolve com 204: 200 sem confirmação vira erro", async () => {
    capture(200, { ok: true });
    await expect(api.unsubscribePush("https://push.example/1", "u1")).rejects.toMatchObject({ status: 200, code: "unexpected_status" });
  });

  it("DELETE 401, 404 e 500 viram ApiError com o status", async () => {
    for (const status of [401, 404, 500]) {
      capture(status, { title: "Erro", status });
      await expect(api.unsubscribePush("https://push.example/1", "u1")).rejects.toMatchObject({ status });
    }
  });

  it("logout 204 sem Set-Cookie resolve; a sessão seguinte é conferida em /me (401 = sem sessão)", async () => {
    let revoked = false;
    vi.stubGlobal("fetch", (u: string) => {
      const path = new URL(u, "http://x").pathname;
      if (path === "/api/auth/logout") {
        revoked = true; // servidor revoga a sessão; o cookie continua no navegador (nenhum Set-Cookie)
        const r = new Response(null, { status: 204 });
        expect(r.headers.get("Set-Cookie")).toBeNull();
        return Promise.resolve(r);
      }
      return Promise.resolve(
        revoked ? new Response(null, { status: 401 }) : new Response(JSON.stringify({ id: "u1" }), { status: 200, headers: { "Content-Type": "application/json" } }),
      );
    });
    expect(await api.me()).toMatchObject({ id: "u1" });
    await expect(api.logout("u1")).resolves.toBeUndefined();
    expect(await api.me()).toBeNull(); // cookie revogado: 401 → sem sessão
  });

  it("logout com cookie já revogado/expirado (401) rejeita com status 401", async () => {
    capture(401, { title: "Não autenticado", status: 401 });
    await expect(api.logout("u1")).rejects.toMatchObject({ status: 401 });
  });

  it("logout envia expectedUserId em JSON", async () => {
    const calls = capture();
    await api.logout("u1");
    expect(calls[0].init?.method).toBe("POST");
    expect(calls[0].init?.credentials).toBe("include");
    expect(new Headers(calls[0].init?.headers).get("Content-Type")).toContain("application/json");
    expect(JSON.parse(String(calls[0].init?.body))).toEqual({ expectedUserId: "u1" });
  });

  it("logout 409 session_changed é rejeitado (sessão mantida)", async () => {
    capture(409, { title: "Sessão alterada", status: 409, detail: "A sessão mudou.", code: "session_changed" });
    await expect(api.logout("u1")).rejects.toMatchObject({ status: 409, code: "session_changed" });
  });
});
