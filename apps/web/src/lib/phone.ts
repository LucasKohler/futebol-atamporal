/** Port of FutebolAtemporal.Domain.Services.PhoneNormalizer */

export type NormalizeResult =
  | { ok: true; phone: string }
  | { ok: false; error: string };

export function tryNormalize(raw: string | null | undefined): NormalizeResult {
  if (raw == null || raw.trim() === "") {
    return { ok: false, error: "Informe o celular." };
  }

  let value = "";
  for (const ch of raw) {
    if (ch >= "0" && ch <= "9") {
      value += ch;
    }
  }

  if (value.startsWith("55") && (value.length === 12 || value.length === 13)) {
    value = value.slice(2);
  }

  if (value.length < 10 || value.length > 11) {
    return { ok: false, error: "Celular inválido. Use DDD + número." };
  }

  return { ok: true, phone: value };
}

export function formatBr(digits: string): string {
  if (digits.length === 11) {
    return `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
  }
  if (digits.length === 10) {
    return `(${digits.slice(0, 2)}) ${digits.slice(2, 6)}-${digits.slice(6)}`;
  }
  return digits;
}