import { describe, expect, it } from "vitest";
import type { RegistrationWindows } from "./api";
import { registrationWindowMessage, windowInputValue, windowInstant } from "./registrationWindows";

const zone = "America/Sao_Paulo";
const windows: RegistrationWindows = {
  isConfigured: true,
  monthlyOpensAt: "2026-11-28T15:00:00Z",
  casualOpensAt: "2026-11-28T17:00:00Z",
  serverNowUtc: "2026-11-28T16:00:00Z",
  myEligibility: "Casual",
  isAdminEarlyBypass: false,
  canJoinNow: false,
  myOpensAt: "2026-11-28T17:00:00Z",
  reasonCode: "CASUAL_WINDOW_NOT_OPEN",
};

describe("registration windows", () => {
  it("round-trips the group wall clock to an absolute UTC instant", () => {
    const iso = "2026-11-28T15:12:34.000Z";
    expect(windowInputValue(iso, zone)).toBe("2026-11-28T12:12:34");
    expect(windowInstant("2026-11-28T12:12:34", zone)).toBe(iso);
    expect(windowInstant("2026-11-28T12:12", zone)).toBe("2026-11-28T15:12:00.000Z");
    expect(windowInstant("", zone)).toBeNull();
  });

  it("rejects missing or ambiguous wall times around daylight saving transitions", () => {
    expect(() => windowInstant("2026-03-08T02:30", "America/New_York")).toThrow(/horário/i);
    expect(() => windowInstant("2026-11-01T01:30", "America/New_York")).toThrow(/horário/i);
  });

  it("uses the server decision and shows different member and admin guidance", () => {
    expect(registrationWindowMessage(windows, zone)).toMatch(/diaristas.*14:00/i);
    expect(registrationWindowMessage({ ...windows, reasonCode: "WINDOW_NOT_OPEN_YET" }, zone)).toMatch(/sua inscrição/i);
    expect(registrationWindowMessage({ ...windows, reasonCode: "ADMIN_EARLY_ACCESS" }, zone)).toMatch(/fim da fila/i);
    expect(registrationWindowMessage({ ...windows, reasonCode: null, canJoinNow: true }, zone)).toBeNull();
  });
});
