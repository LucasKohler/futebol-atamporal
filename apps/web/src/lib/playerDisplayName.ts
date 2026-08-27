/** Port of FutebolAtemporal.Domain.Services.PlayerDisplayName.Effective */

const FALLBACK = "Jogador";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function usable(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  if (!trimmed || GUID.test(trimmed)) return null;
  return trimmed;
}

export function playerDisplayName(
  registeredName: string | null | undefined,
  nickname?: string | null,
): string {
  return usable(nickname) ?? usable(registeredName) ?? FALLBACK;
}

export const effective = playerDisplayName;