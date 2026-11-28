import type { RegistrationWindows } from "./api";

function wallClockFormat(zone: string) {
  return new Intl.DateTimeFormat("sv-SE", {
    timeZone: zone, year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23",
  });
}

export function windowInputValue(instant: string | null, zone: string): string {
  return instant ? wallClockFormat(zone).format(new Date(instant)).replace(" ", "T") : "";
}

export function windowInstant(value: string, zone: string): string | null {
  if (!value) return null;
  const local = value.length === 16 ? `${value}:00` : value;
  const estimate = Date.parse(`${local}Z`);
  if (!Number.isFinite(estimate)) throw new Error("Informe uma data e hora válidas.");

  // Compare offsets on both sides of a possible daylight-saving transition.
  const offsets = new Set([-1, 0, 1].map((days) => {
    const probe = estimate + days * 86_400_000;
    return Date.parse(`${windowInputValue(new Date(probe).toISOString(), zone)}Z`) - probe;
  }));
  const candidates = [...offsets].map((offset) => new Date(estimate - offset).toISOString())
    .filter((instant) => windowInputValue(instant, zone) === local);

  if (candidates.length !== 1)
    throw new Error("Este horário é inválido ou ambíguo no fuso do grupo. Escolha outro horário.");
  return candidates[0];
}

export function formatRegistrationTime(instant: string, zone: string): string {
  return new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short", timeZone: zone }).format(new Date(instant));
}

export function registrationWindowMessage(windows: RegistrationWindows, zone: string): string | null {
  if (windows.reasonCode === "ADMIN_EARLY_ACCESS") return "Acesso antecipado de admin: entrada no fim da fila de espera.";
  if (windows.reasonCode === "STATUS_CLOSED") return "As inscrições estão fechadas.";
  if (!windows.canJoinNow && windows.myOpensAt) {
    const opens = formatRegistrationTime(windows.myOpensAt, zone);
    return windows.reasonCode === "CASUAL_WINDOW_NOT_OPEN"
      ? `A inscrição de diaristas abre em ${opens}.`
      : `Sua inscrição abre em ${opens}.`;
  }
  return null;
}
