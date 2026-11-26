/**
 * Stepper operacional da rodada semanal: Lista & Presença → Sorteio 3 Cores → Em Jogo → Súmula Pós-jogo.
 * Derivado só do estado persistido pela API (status da partida + sorteio); o cliente não guarda etapa própria.
 * As transições continuam validadas pela API (MatchTransitions); aqui só se escolhe o próximo passo sugerido.
 */

export type StepNumber = 1 | 2 | 3 | 4;
export type StepStatus = "done" | "current" | "upcoming";

export const ROUND_STEPS: { step: StepNumber; label: string; short: string }[] = [
  { step: 1, label: "Lista & Presença", short: "Lista" },
  { step: 2, label: "Sorteio 3 Cores", short: "Sorteio" },
  { step: 3, label: "Em Jogo", short: "Jogo" },
  { step: 4, label: "Súmula Pós-jogo", short: "Súmula" },
];

export type RoundCta =
  | { kind: "link"; label: string; href: string }
  | { kind: "transition"; label: string; to: "RegistrationOpen" | "InProgress" | "AwaitingClose"; href: string };

export type RoundInput = {
  status: string | null;
  confirmedCount: number;
  draw: { status: string; rosterChanged: boolean; canConfirm: boolean } | null;
};

export type RoundStepView = {
  current: StepNumber;
  /** Rodada inteira concluída (todas as etapas feitas). */
  finished: boolean;
  steps: { step: StepNumber; label: string; short: string; status: StepStatus }[];
  headline: string;
  blocker: string | null;
  cta: RoundCta | null;
};

export const MIN_PLAYERS_TO_DRAW = 3;

function view(current: StepNumber, headline: string, cta: RoundCta | null, blocker: string | null = null, finished = false): RoundStepView {
  return {
    current,
    finished,
    headline,
    blocker,
    cta,
    steps: ROUND_STEPS.map((s) => ({
      ...s,
      status: finished || s.step < current ? "done" : s.step === current ? "current" : "upcoming",
    })),
  };
}

const createMatch: RoundCta = { kind: "link", label: "Criar partida", href: "/partida" };
const goDraw = (label: string): RoundCta => ({ kind: "link", label, href: "/admin/sorteio" });
const goSheet: RoundCta = { kind: "link", label: "Lançar súmula", href: "/admin/registro" };

export function deriveRoundStep({ status, confirmedCount, draw }: RoundInput): RoundStepView {
  const tooFew =
    confirmedCount < MIN_PLAYERS_TO_DRAW ? `São necessários ao menos ${MIN_PLAYERS_TO_DRAW} confirmados para sortear (hoje: ${confirmedCount}).` : null;

  switch (status) {
    case null:
    case undefined:
      return view(1, "Nenhuma partida aberta", createMatch);
    case "Cancelled":
      return view(1, "Partida cancelada", createMatch);
    case "Draft":
      return view(1, "Lista ainda não aberta", { kind: "transition", label: "Abrir inscrições", to: "RegistrationOpen", href: "/partida" });
    case "RegistrationOpen":
      return tooFew
        ? view(1, "Inscrições abertas", { kind: "link", label: "Ver lista", href: "/partida" }, tooFew)
        : view(1, "Inscrições abertas", goDraw("Sortear times"));
    case "RegistrationClosed": {
      if (tooFew) return view(2, "Lista encerrada", { kind: "link", label: "Ver lista", href: "/partida" }, tooFew);
      if (!draw) return view(2, "Lista encerrada, sorteio pendente", goDraw("Sortear times"));
      if (draw.rosterChanged)
        return view(2, "A lista mudou depois do sorteio", goDraw("Refazer sorteio"), "Refaça o sorteio antes de confirmar.");
      if (!draw.canConfirm)
        return view(2, "Prévia do sorteio com bloqueios", goDraw("Revisar sorteio"), "O sorteio tem bloqueios que impedem a confirmação.");
      return view(2, "Prévia pronta para confirmar", goDraw("Revisar e confirmar escalação"));
    }
    case "Drawn":
      if (draw?.rosterChanged)
        return view(2, "A lista mudou depois da confirmação", goDraw("Refazer sorteio"), "A escalação confirmada ficou inválida.");
      return view(3, "Escalação confirmada", { kind: "transition", label: "Iniciar partida", to: "InProgress", href: "/partida" });
    case "InProgress":
      return view(3, "Partida em andamento", goSheet);
    case "AwaitingClose":
      return view(4, "Aguardando a súmula", goSheet);
    case "Completed":
      return view(4, "Rodada encerrada", { kind: "link", label: "Criar próxima partida", href: "/partida" }, null, true);
    default:
      return view(1, status, { kind: "link", label: "Ver lista", href: "/partida" });
  }
}
