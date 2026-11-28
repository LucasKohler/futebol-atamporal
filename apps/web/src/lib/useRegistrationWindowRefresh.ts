"use client";

import { useEffect } from "react";
import type { MatchDetail } from "./api";

export function useRegistrationWindowRefresh(detail: MatchDetail | null, refresh: (matchId: string) => Promise<void>) {
  const id = detail?.match.id;
  const window = detail?.registrationWindows;
  useEffect(() => {
    if (!id || !window?.myOpensAt) return;
    const remaining = Date.parse(window.myOpensAt) - Date.parse(window.serverNowUtc);
    if (remaining <= 0) return;
    const refreshMatch = () => { void refresh(id).catch(() => undefined); };
    const timer = globalThis.setTimeout(refreshMatch, Math.min(remaining + 100, 2_147_483_647));
    const refreshOnReturn = () => {
      if (document.visibilityState === "visible") refreshMatch();
    };
    document.addEventListener("visibilitychange", refreshOnReturn);
    return () => {
      globalThis.clearTimeout(timer);
      document.removeEventListener("visibilitychange", refreshOnReturn);
    };
  }, [id, window, refresh]);
}
