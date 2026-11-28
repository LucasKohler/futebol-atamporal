import { describe, expect, it } from "vitest";
import {
  canLogoutAfter,
  revocationMessage,
  revocationOutcome,
  arrayBufferToBase64Url,
  broadcastIssues,
  cooldownText,
  deviceLabel,
  detectPlatform,
  dismissUntil,
  installBannerMode,
  isAllowedActionUrl,
  isDismissed,
  pushState,
  relativeTime,
  startsWithEmoji,
  takeNotificationParam,
  urlBase64ToUint8Array,
} from "./notifications";

const IPHONE_SAFARI = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Mobile/15E148 Safari/604.1";
const IPHONE_CHROME = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0 Mobile/15E148 Safari/604.1";
const ANDROID = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36";
const IPAD_DESKTOP_UA = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15";
const NOW = Date.parse("2026-11-26T20:00:00Z");

describe("plataforma e banner de instalação", () => {
  it("detecta iPhone Safari, iPhone Chrome, iPadOS (toque) e Android", () => {
    expect(detectPlatform(IPHONE_SAFARI)).toEqual({ isIos: true, isAndroid: false, isIosSafari: true });
    expect(detectPlatform(IPHONE_CHROME).isIosSafari).toBe(false);
    expect(detectPlatform(IPAD_DESKTOP_UA, 5).isIos).toBe(true);
    expect(detectPlatform(IPAD_DESKTOP_UA, 0).isIos).toBe(false);
    expect(detectPlatform(ANDROID).isAndroid).toBe(true);
  });

  it("instalado esconde; prompt dá botão de 1 toque; Safari iOS mostra guia; dispensado esconde", () => {
    const ios = detectPlatform(IPHONE_SAFARI);
    const android = detectPlatform(ANDROID);
    expect(installBannerMode({ standalone: true, hasPrompt: true, platform: android, dismissed: false })).toBe("hidden");
    expect(installBannerMode({ standalone: false, hasPrompt: true, platform: android, dismissed: false })).toBe("prompt");
    expect(installBannerMode({ standalone: false, hasPrompt: false, platform: ios, dismissed: false })).toBe("ios-guide");
    expect(installBannerMode({ standalone: false, hasPrompt: false, platform: detectPlatform(IPHONE_CHROME), dismissed: false })).toBe("hidden");
    expect(installBannerMode({ standalone: false, hasPrompt: true, platform: android, dismissed: true })).toBe("hidden");
  });

  it("dispensar vale 14 dias", () => {
    const until = dismissUntil(NOW);
    expect(isDismissed(String(until), NOW + 13 * 86_400_000)).toBe(true);
    expect(isDismissed(String(until), NOW + 15 * 86_400_000)).toBe(false);
    expect(isDismissed(null, NOW)).toBe(false);
    expect(isDismissed("lixo", NOW)).toBe(false);
  });
});

describe("estado do push no aparelho", () => {
  const base = {
    hasNotification: true,
    hasServiceWorker: true,
    hasPushManager: true,
    platform: detectPlatform(ANDROID),
    standalone: false,
    permission: "default" as NotificationPermission,
    serverMode: "Live",
    subscribed: false,
  };
  it("iPhone fora da Tela de Início precisa instalar antes", () => {
    expect(pushState({ ...base, platform: detectPlatform(IPHONE_SAFARI), hasPushManager: false })).toBe("ios-needs-install");
    expect(pushState({ ...base, platform: detectPlatform(IPHONE_SAFARI), standalone: true })).toBe("off");
  });
  it("sem suporte, servidor desligado, negado, desligado e ligado", () => {
    expect(pushState({ ...base, hasPushManager: false })).toBe("unsupported");
    expect(pushState({ ...base, serverMode: "Disabled" })).toBe("server-disabled");
    expect(pushState({ ...base, permission: "denied" })).toBe("denied");
    expect(pushState(base)).toBe("off");
    expect(pushState({ ...base, permission: "granted", subscribed: true })).toBe("on");
    expect(pushState({ ...base, permission: "granted", subscribed: false })).toBe("off");
  });

  it("remoção no servidor confirmada com inscrição local restante: mostra Ativar", () => {
    expect(pushState({ ...base, permission: "granted", subscribed: true, serverRevoked: true })).toBe("off");
  });

  it("revogação falha ou incerta (só a marca de desligado, sem confirmação): continua oferecendo Desativar", () => {
    expect(pushState({ ...base, permission: "granted", subscribed: true, serverRevoked: false })).toBe("on");
    expect(pushState({ ...base, permission: "granted", subscribed: true })).toBe("on");
  });

  it("sem Web Locks: ativar fica indisponível (needs-update), mas um aparelho ainda ligado pode ser desligado", () => {
    expect(pushState({ ...base, permission: "granted", subscribed: false, hasLocks: false })).toBe("needs-update");
    expect(pushState({ ...base, permission: "granted", subscribed: true, serverRevoked: true, hasLocks: false })).toBe("needs-update");
    expect(pushState({ ...base, permission: "granted", subscribed: true, hasLocks: false })).toBe("on");
    expect(pushState({ ...base, permission: "denied", hasLocks: false })).toBe("denied");
  });
});

describe("destinos, ?n= e comunicado", () => {
  it("só os 5 destinos exatos", () => {
    expect(isAllowedActionUrl("/partida")).toBe(true);
    expect(isAllowedActionUrl(" /perfil ")).toBe(true);
    for (const bad of ["javascript:alert(1)", "//evil.com", "https://evil.com", "/admin", "/partida/x", ""]) expect(isAllowedActionUrl(bad)).toBe(false);
  });

  it("tira ?n={id} da URL preservando os outros parâmetros; id inválido é ignorado", () => {
    const id = "0f8fad5b-d9cb-469f-a165-70867728950e";
    expect(takeNotificationParam(`https://app.test/partida?n=${id}&x=1#y`)).toEqual({ id, cleanUrl: "/partida?x=1#y" });
    expect(takeNotificationParam("https://app.test/?n=<script>")).toEqual({ id: null, cleanUrl: "/" });
    expect(takeNotificationParam("https://app.test/rankings")).toEqual({ id: null, cleanUrl: "/rankings" });
  });

  it("valida título (120), corpo (500) e destino", () => {
    expect(broadcastIssues("Oi", "Jogo hoje", "/partida")).toEqual([]);
    expect(broadcastIssues("", "", "/partida")).toEqual(["Escreva um título.", "Escreva a mensagem."]);
    expect(broadcastIssues("x".repeat(121), "y".repeat(501), "/admin")).toHaveLength(3);
  });

  it("textos de apoio", () => {
    expect(cooldownText(600)).toMatch(/10 min/);
    expect(cooldownText(null)).toMatch(/há pouco/);
    expect(relativeTime("2026-11-26T19:59:30Z", NOW)).toBe("agora");
    expect(relativeTime("2026-11-26T19:15:00Z", NOW)).toBe("há 45 min");
    expect(relativeTime("2026-11-26T17:00:00Z", NOW)).toBe("há 3 h");
    expect(relativeTime("2026-11-25T12:00:00Z", NOW)).toBe("ontem");
    expect(deviceLabel(ANDROID)).toBe("Android");
    expect(deviceLabel(IPHONE_SAFARI)).toBe("iPhone/iPad");
  });
});

describe("chaves Base64Url", () => {
  it("decodifica a chave pública VAPID (65 bytes) e codifica chaves da inscrição sem padding", () => {
    const bytes = new Uint8Array(65).map((_, i) => (i * 7) % 256);
    bytes[0] = 4; // ponto P-256 descomprimido
    const b64url = arrayBufferToBase64Url(bytes.buffer);
    expect(b64url).not.toMatch(/[+/=]/);
    const back = urlBase64ToUint8Array(b64url);
    expect(back.length).toBe(65);
    expect([...back]).toEqual([...bytes]);
  });
});

describe("ícone sem duplicar emoji do título do servidor", () => {
  it("detecta título que já começa com emoji", () => {
    expect(startsWithEmoji("⚡ Vaga confirmada!")).toBe(true);
    expect(startsWithEmoji("  📋 Lembrete")).toBe(true);
    expect(startsWithEmoji("Jogo confirmado")).toBe(false);
    expect(startsWithEmoji("1º lugar")).toBe(false);
  });
});

describe("revogação do push no logout e no botão Desativar", () => {
  const base = { readFailed: false, subscriptionFound: true };
  it("classifica o resultado pelas duas pontas", () => {
    expect(revocationOutcome({ ...base, subscriptionFound: false, serverRevoked: false, browserRevoked: false })).toEqual({ status: "none" });
    expect(revocationOutcome({ ...base, serverRevoked: true, browserRevoked: true })).toEqual({ status: "revoked" });
    expect(revocationOutcome({ ...base, serverRevoked: false, browserRevoked: true })).toMatchObject({ status: "partial" });
    expect(revocationOutcome({ ...base, serverRevoked: false, browserRevoked: false })).toEqual({ status: "failed", reason: "both" });
    expect(revocationOutcome({ ...base, readFailed: true, serverRevoked: false, browserRevoked: false })).toEqual({ status: "failed", reason: "read" });
  });

  it("só permite sair com o push pessoal comprovadamente desligado", () => {
    expect(canLogoutAfter({ status: "none" })).toBe(true);
    expect(canLogoutAfter({ status: "revoked" })).toBe(true);
    expect(canLogoutAfter({ status: "partial", serverRevoked: true, browserRevoked: false })).toBe(true);
    expect(canLogoutAfter({ status: "failed", reason: "both" })).toBe(false);
    expect(canLogoutAfter({ status: "failed", reason: "read" })).toBe(false);
  });

  it("mensagens: falha nunca anuncia sucesso; logout bloqueado explica que a conta segue conectada", () => {
    expect(revocationMessage({ status: "failed", reason: "both" }, "toggle")).toMatch(/continuam ativos/);
    expect(revocationMessage({ status: "failed", reason: "read" }, "logout")).toMatch(/continua conectado/);
    expect(revocationMessage({ status: "revoked" }, "toggle")).toMatch(/desativados/);
    expect(revocationMessage({ status: "revoked" }, "logout")).toBeNull();
    expect(revocationMessage({ status: "partial", serverRevoked: false, browserRevoked: true }, "toggle")).toMatch(/não recebe mais/);
  });
});
