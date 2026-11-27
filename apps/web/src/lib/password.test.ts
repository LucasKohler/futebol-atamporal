import { describe, expect, it } from "vitest";
import { adminRecoveryError, adminSetupError, loginErrorMessage, passwordChecks, passwordProblem, registerErrorMessage } from "./password";
import { isLaunchedOpenFee, isSettled, matchFeeBadge } from "./pix";

describe("política de senha (espelho do Identity atual)", () => {
  it("aceita 8+ com maiúscula, minúscula e número", () => {
    expect(passwordProblem("Futebol1")).toBeNull();
  });

  it("explica em português o que falta", () => {
    expect(passwordProblem("")).toBe("Informe a senha.");
    expect(passwordProblem("abc")).toBe("A senha precisa ter 8 caracteres ou mais, uma letra maiúscula e um número.");
    expect(passwordProblem("futebol1")).toBe("A senha precisa ter uma letra maiúscula.");
  });

  it("lista de requisitos marca cada regra", () => {
    expect(passwordChecks("Ab1").map((c) => c.ok)).toEqual([false, true, true, true]);
  });
});

describe("mensagens da API de login/cadastro", () => {
  it("login usa o code da API", () => {
    expect(loginErrorMessage("invalid_credentials", "x")).toBe("Celular ou senha inválidos.");
    expect(loginErrorMessage("locked_out", "x")).toMatch(/bloqueada/);
    expect(loginErrorMessage(null, "Erro de rede")).toBe("Erro de rede");
  });
  it("regra de senha em inglês vira português", () => {
    expect(registerErrorMessage("Passwords must have at least one uppercase ('A'-'Z').", "futebol1")).toBe("A senha precisa ter uma letra maiúscula.");
    expect(registerErrorMessage("Já existe uma conta com este celular.", "Futebol1")).toBe("Já existe uma conta com este celular.");
  });
});

describe("configuração inicial do admin (código de uso único)", () => {
  it("mapeia os códigos de erro da API", () => {
    expect(adminSetupError("invalid_setup_code", "x", "")).toMatchObject({ field: "setupCode", final: false });
    expect(adminSetupError("admin_phone_mismatch", "x", "")).toMatchObject({ field: "phone", final: false });
    expect(adminSetupError("weak_password", "x", "futebol1")).toEqual({ message: "A senha precisa ter uma letra maiúscula.", field: "password", final: false });
    expect(adminSetupError("setup_already_done", "x", "")).toMatchObject({ field: null, final: true });
    expect(adminSetupError("setup_disabled", "x", "")).toMatchObject({ final: true });
    expect(adminSetupError(null, "Erro de rede", "")).toEqual({ message: "Erro de rede", field: null, final: false });
  });
});

describe("recuperação do admin (código temporário)", () => {
  it("mapeia os códigos de erro da API", () => {
    expect(adminRecoveryError("invalid_recovery_code", "x", "")).toMatchObject({ field: "setupCode", final: false });
    expect(adminRecoveryError("admin_phone_mismatch", "x", "")).toMatchObject({ field: "phone", final: false });
    expect(adminRecoveryError("weak_password", "x", "futebol1").message).toBe("A senha precisa ter uma letra maiúscula.");
    for (const c of ["recovery_code_expired", "recovery_code_used", "recovery_disabled"]) {
      expect(adminRecoveryError(c, "x", "")).toMatchObject({ field: null, final: true });
    }
    expect(adminRecoveryError("setup_pending", "x", "")).toMatchObject({ setupPending: true, final: false });
    expect(adminRecoveryError("recovery_conflict", "x", "")).toMatchObject({ field: null, final: false });
    expect(adminRecoveryError(null, "Erro de rede", "")).toEqual({ message: "Erro de rede", field: null, final: false });
  });
});

describe("diária NotCharged (rodada ainda não fechada)", () => {
  it("mostra 'Sem cobrança ainda' e nunca é cobrável", () => {
    expect(matchFeeBadge("NotCharged", null)).toEqual({ label: "Sem cobrança ainda", tone: "neutral" });
    expect(isLaunchedOpenFee({ status: "NotCharged", dueAt: null })).toBe(false);
    expect(isSettled("NotCharged")).toBe(false);
  });
});
