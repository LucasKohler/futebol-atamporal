/**
 * Política de senha atual do backend (ASP.NET Identity já configurado, sem mudança nesta etapa):
 * mínimo de 8 caracteres, com letra maiúscula, letra minúscula e número.
 * A validação aqui só antecipa a mensagem em português; a API continua sendo a regra.
 */
export const PASSWORD_RULES: { id: string; label: string; test: (p: string) => boolean }[] = [
  { id: "length", label: "8 caracteres ou mais", test: (p) => p.length >= 8 },
  { id: "upper", label: "uma letra maiúscula", test: (p) => /[A-Z]/.test(p) },
  { id: "lower", label: "uma letra minúscula", test: (p) => /[a-z]/.test(p) },
  { id: "digit", label: "um número", test: (p) => /\d/.test(p) },
];

export function passwordChecks(password: string) {
  return PASSWORD_RULES.map((r) => ({ id: r.id, label: r.label, ok: r.test(password) }));
}

/** Mensagem curta com o que falta, ou null quando a senha atende à política. */
export function passwordProblem(password: string): string | null {
  if (!password) return "Informe a senha.";
  const missing = passwordChecks(password).filter((c) => !c.ok).map((c) => c.label);
  return missing.length ? `A senha precisa ter ${joinPt(missing)}.` : null;
}

/** Mensagem do login por código da API (401): sempre genérica para credencial inválida. */
export function loginErrorMessage(code: string | null | undefined, fallback: string): string {
  switch (code) {
    case "invalid_credentials":
      return "Celular ou senha inválidos.";
    case "locked_out":
      return "Muitas tentativas erradas. A conta ficou bloqueada por alguns minutos. Tente mais tarde ou peça ao admin para redefinir a senha.";
    case "invalid_phone":
      return "Celular inválido. Use DDD + número.";
    default:
      return fallback;
  }
}

export type AdminSetupError = { message: string; field: "setupCode" | "phone" | "password" | null; final: boolean };

/** Erros do POST /api/auth/admin-setup por código. `final` = a configuração não está (mais) disponível. */
export function adminSetupError(code: string | null | undefined, fallback: string, password: string): AdminSetupError {
  switch (code) {
    case "invalid_setup_code":
      return { message: "Código de configuração inválido. Confira com quem opera o servidor.", field: "setupCode", final: false };
    case "admin_phone_mismatch":
      return { message: "Este celular não é o configurado para o admin.", field: "phone", final: false };
    case "password_required":
      return { message: "Informe a senha.", field: "password", final: false };
    case "weak_password":
      return { message: passwordProblem(password) ?? "A senha não atende à regra.", field: "password", final: false };
    case "setup_already_done":
      return { message: "O admin já foi configurado e este código não vale mais. Entre com celular e senha.", field: null, final: true };
    case "setup_disabled":
      return { message: "A configuração inicial não está disponível neste servidor.", field: null, final: true };
    default:
      return { message: fallback, field: null, final: false };
  }
}

/** Erros do POST /api/auth/admin-recovery. `final` = sem recuperação possível com este código agora. */
export function adminRecoveryError(
  code: string | null | undefined,
  fallback: string,
  password: string,
): AdminSetupError & { setupPending?: boolean } {
  switch (code) {
    case "invalid_recovery_code":
      return { message: "Código de recuperação inválido. Confira com quem opera o servidor.", field: "setupCode", final: false };
    case "admin_phone_mismatch":
      return { message: "Este celular não é o configurado para o admin.", field: "phone", final: false };
    case "password_required":
      return { message: "Informe a nova senha.", field: "password", final: false };
    case "weak_password":
      return { message: passwordProblem(password) ?? "A senha não atende à regra.", field: "password", final: false };
    case "recovery_conflict":
      // Tentativa simultânea ou falha de gravação: nada mudou e o mesmo código pode ser usado de novo.
      return { message: "Não foi possível concluir agora e nada foi alterado. Tente de novo em instantes.", field: null, final: false };
    case "recovery_code_expired":
      return { message: "Este código de recuperação venceu. Peça um novo a quem opera o servidor.", field: null, final: true };
    case "recovery_code_used":
      return { message: "Este código de recuperação já foi usado. Peça um novo a quem opera o servidor.", field: null, final: true };
    case "recovery_disabled":
      return { message: "Não há recuperação ativa neste servidor. Peça a quem opera o servidor um código temporário.", field: null, final: true };
    case "setup_pending":
      return { message: "O admin ainda não foi configurado. Use a configuração inicial.", field: null, final: false, setupPending: true };
    default:
      return { message: fallback, field: null, final: false };
  }
}

/** A API devolve a regra de senha do Identity em inglês; na tela mostramos a versão em português. */
export function registerErrorMessage(detail: string, password: string): string {
  return /password/i.test(detail) ? (passwordProblem(password) ?? "A senha não atende à regra.") : detail;
}

function joinPt(items: string[]): string {
  return items.length <= 1 ? items.join("") : `${items.slice(0, -1).join(", ")} e ${items[items.length - 1]}`;
}
