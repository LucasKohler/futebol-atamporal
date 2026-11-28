/* Futebol Atemporal — service worker só para push (contrato PWA/notificações D7/D8).
 * Sem cache offline e sem handler de fetch: nada de dado autenticado fica guardado no aparelho.
 * No clique, só abre rotas da MESMA origem e da lista permitida, acrescentando ?n={id} para o app marcar como lida.
 */
"use strict";

var ALLOWED_PATHS = ["/", "/partida", "/pagamentos", "/rankings", "/perfil"];
var DEFAULT_TITLE = "Futebol Atemporal";

/** Payload do servidor: { id, title, body, url, tag }. Texto puro, limitado (título 120, corpo 500). */
function parsePayload(data) {
  var raw = {};
  try {
    raw = data ? data.json() : {};
  } catch (e) {
    try {
      raw = { body: data ? data.text() : "" };
    } catch (e2) {
      raw = {};
    }
  }
  var str = function (v, max) {
    return typeof v === "string" ? v.slice(0, max) : "";
  };
  return {
    id: str(raw.id, 64),
    title: str(raw.title, 120) || DEFAULT_TITLE,
    body: str(raw.body, 500),
    url: str(raw.url, 200),
    tag: str(raw.tag, 120),
  };
}

/** Destino seguro: rota exata da lista permitida, na origem do próprio app. Qualquer outra coisa vira "/". */
function safeTarget(url, id, origin) {
  var path = "/";
  if (typeof url === "string") {
    var trimmed = url.trim();
    if (ALLOWED_PATHS.indexOf(trimmed) !== -1) path = trimmed;
  }
  var target = new URL(path, origin);
  if (id && /^[0-9a-fA-F-]{36}$/.test(id)) target.searchParams.set("n", id);
  return target.href;
}

self.addEventListener("install", function () {
  self.skipWaiting();
});

self.addEventListener("activate", function (event) {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("push", function (event) {
  var p = parsePayload(event.data);
  var options = {
    body: p.body,
    icon: "/icons/icon-192.png",
    badge: "/icons/badge-96.png",
    data: { id: p.id, url: p.url },
  };
  if (p.tag) {
    options.tag = p.tag; // avisos repetidos se substituem no aparelho
    options.renotify = true;
  }
  event.waitUntil(self.registration.showNotification(p.title, options));
});

self.addEventListener("notificationclick", function (event) {
  event.notification.close();
  var data = event.notification.data || {};
  var origin = self.location.origin;
  var target = safeTarget(data.url, data.id, origin);
  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then(function (list) {
      for (var i = 0; i < list.length; i++) {
        var client = list[i];
        if (client.url && client.url.indexOf(origin) === 0 && "focus" in client) {
          return client.focus().then(function (c) {
            return c && "navigate" in c ? c.navigate(target) : c;
          });
        }
      }
      return self.clients.openWindow ? self.clients.openWindow(target) : undefined;
    }),
  );
});

// Exposto só para testes automatizados (vm); não altera o comportamento no navegador.
self.__faSw = { parsePayload: parsePayload, safeTarget: safeTarget, ALLOWED_PATHS: ALLOWED_PATHS };
