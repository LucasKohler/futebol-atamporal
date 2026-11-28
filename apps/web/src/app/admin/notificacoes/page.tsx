"use client";

import { useCallback, useEffect, useState } from "react";
import { AdminCard, AdminEmpty, AdminError, AdminKpiStrip, AdminLoading, AdminNotice, StatusBadge } from "@/components/admin/AdminCard";
import { AdminConfirmButton } from "@/components/admin/AdminDrawer";
import { AdminShell } from "@/components/admin/AdminShell";
import { inputClass, primaryButton, secondaryButton } from "@/components/admin/ui";
import {
  api,
  ApiError,
  type AdminNotificationSummary,
  type BroadcastAudience,
  type NotificationActionUrl,
  type NotificationCampaign,
  type QuickReminderKind,
} from "@/lib/api";
import {
  ACTION_URLS,
  AUDIENCE_LABEL,
  BODY_MAX,
  broadcastIssues,
  CATEGORY_ICON,
  CATEGORY_LABEL,
  cooldownText,
  newIdempotencyKey,
  SOURCE_LABEL,
  startsWithEmoji,
  TITLE_MAX,
} from "@/lib/notifications";
import { useSession } from "@/lib/useSession";

type Preview = { recipients: number; pushDevices: number };

const QUICK: { kind: QuickReminderKind; icon: string; title: string; text: string }[] = [
  { kind: "RegistrationReminder", icon: "📝", title: "Lembrete de presença", text: "Pede para confirmar ou desistir da lista da partida aberta." },
  { kind: "PaymentReminder", icon: "💳", title: "Lembrete de pagamento", text: "Vai só para quem tem mensalidade ou diária pendente. O texto não mostra valores." },
  { kind: "MatchAvailable", icon: "⚽", title: "Partida disponível", text: "Avisa todos os membros ativos que há partida para entrar." },
];

const BROADCAST_AUDIENCES: BroadcastAudience[] = ["AllMembers", "MatchConfirmed", "PaymentPending"];

/** Prefixo de ícone só quando o título ainda não começa com um emoji. */
function withoutRepeatedIcon(title: string, icon: string): string {
  return startsWithEmoji(title) ? "" : `${icon} `;
}

function errorText(e: unknown): string {
  if (e instanceof ApiError && e.code === "reminder_cooldown") return cooldownText(e.retryAfterSeconds);
  if (e instanceof ApiError && e.code === "no_recipients") return "Ninguém nesse público agora. Nada foi enviado.";
  return e instanceof Error ? e.message : "Erro ao enviar.";
}

/** Central de notificações do admin: KPIs, lembretes rápidos, comunicado com prévia e histórico. */
export default function AdminNotificacoesPage() {
  const { group, isAdmin } = useSession();
  const [summary, setSummary] = useState<AdminNotificationSummary | null>(null);
  const [campaigns, setCampaigns] = useState<{ items: NotificationCampaign[]; total: number } | null>(null);
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!group) return;
    const [s, c] = await Promise.all([api.adminNotificationSummary(group.id), api.adminCampaigns(group.id, 1, 20)]);
    setSummary(s);
    setCampaigns(c);
    setPage(1);
  }, [group]);

  useEffect(() => {
    if (!group || !isAdmin) return;
    load().catch((e) => setError(e instanceof Error ? e.message : "Erro ao carregar."));
  }, [group, isAdmin, load]);

  async function more() {
    if (!group) return;
    const next = page + 1;
    const c = await api.adminCampaigns(group.id, next, 20);
    setCampaigns((prev) => ({ items: [...(prev?.items ?? []), ...c.items], total: c.total }));
    setPage(next);
  }

  const sent = (msg: string) => {
    setNotice(msg);
    setError(null);
    load().catch(() => undefined);
  };

  return (
    <AdminShell
      moduleId="notificacoes"
      title="Notificações"
      description="Avisos chegam sempre no sininho do app; no celular, só para quem ativou o push. Os números contam envios aceitos pelo serviço de push, não confirmação de entrega."
    >
      {error ? <AdminError>{error}</AdminError> : null}
      {notice ? <AdminNotice>{notice}</AdminNotice> : null}

      {!summary ? (
        error ? null : <AdminLoading label="Carregando…" />
      ) : (
        <>
          <section aria-labelledby="kpis-notif" className="flex flex-col gap-sm">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 id="kpis-notif" className="text-headline-md">
                Alcance
              </h2>
              <StatusBadge tone={summary.pushMode === "Live" ? "success" : summary.pushMode === "Simulator" ? "warning" : "danger"}>
                {summary.pushMode === "Live" ? "Push ativo" : summary.pushMode === "Simulator" ? "Push simulado (teste)" : "Push desligado: só no app"}
              </StatusBadge>
            </div>
            <AdminKpiStrip
              label="Indicadores de notificações"
              items={[
                { label: "Membros ativos", value: summary.membersActive },
                { label: "Com push ativo", value: summary.membersWithPush, hint: `${summary.activeDevices} aparelho(s)` },
                // sentLast30d = soma dos destinatários das campanhas (avisos no app), não pushes aceitos.
                { label: "Avisos no app (30 dias)", value: summary.sentLast30d, hint: "notificações criadas" },
                {
                  label: "Último envio",
                  value: summary.lastCampaignAt ? new Date(summary.lastCampaignAt).toLocaleDateString("pt-BR") : "—",
                  hint: summary.lastCampaignAt ? new Date(summary.lastCampaignAt).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" }) : undefined,
                },
              ]}
            />
            <p className="text-label-md text-on-surface-variant">No iPhone, o push só chega com o app instalado na Tela de Início (iOS 16.4+).</p>
          </section>

          <section aria-labelledby="rapidos" className="flex flex-col gap-sm">
            <h2 id="rapidos" className="text-headline-md">
              Lembretes rápidos
            </h2>
            <div className="grid gap-sm md:grid-cols-3">
              {QUICK.map((q) => (
                <QuickReminder key={q.kind} groupId={group!.id} {...q} onSent={sent} onError={setError} />
              ))}
            </div>
            <p className="text-label-md text-on-surface-variant">O mesmo lembrete só pode ser repetido depois de 30 minutos.</p>
          </section>

          <Broadcast groupId={group!.id} onSent={sent} />

          <section aria-labelledby="historico-notif" className="flex flex-col gap-sm">
            <h2 id="historico-notif" className="text-headline-md">
              Histórico
            </h2>
            {!campaigns || campaigns.items.length === 0 ? (
              <AdminEmpty>Nenhum aviso enviado ainda.</AdminEmpty>
            ) : (
              <ul className="overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
                {campaigns.items.map((c) => (
                  <li key={c.id} className="flex flex-col gap-1 border-b border-outline-variant px-md py-sm last:border-0">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <p className="font-semibold">
                        <span aria-hidden="true">{withoutRepeatedIcon(c.title, CATEGORY_ICON[c.category] ?? "🔔")}</span>
                        {c.title}
                      </p>
                      <StatusBadge tone={c.source === "Automatic" ? "neutral" : "info"}>{SOURCE_LABEL[c.source] ?? c.source}</StatusBadge>
                    </div>
                    <p className="text-label-md text-on-surface-variant">
                      {new Date(c.createdAt).toLocaleString("pt-BR")} · {CATEGORY_LABEL[c.category] ?? c.category} · {AUDIENCE_LABEL[c.audience] ?? c.audience}
                      {c.createdByName ? ` · por ${c.createdByName}` : ""}
                    </p>
                    <p className="text-body-md">
                      {c.recipientCount} no app · {c.pushSentCount} de {c.pushDeviceCount} enviados ao celular
                      {c.pushFailedCount ? ` · ${c.pushFailedCount} falharam` : ""}
                    </p>
                  </li>
                ))}
              </ul>
            )}
            {campaigns && campaigns.items.length < campaigns.total ? (
              <button type="button" className={`${secondaryButton} self-start`} onClick={() => more().catch((e) => setError(errorText(e)))}>
                Ver mais
              </button>
            ) : null}
          </section>
        </>
      )}
    </AdminShell>
  );
}

function QuickReminder({
  groupId,
  kind,
  icon,
  title,
  text,
  onSent,
  onError,
}: {
  groupId: string;
  kind: QuickReminderKind;
  icon: string;
  title: string;
  text: string;
  onSent: (msg: string) => void;
  onError: (msg: string | null) => void;
}) {
  const [audience, setAudience] = useState<"NotResponded" | "AllMembers">("NotResponded");
  const [preview, setPreview] = useState<Preview | null>(null);
  // Uma chave por intenção de envio: clique duplo ou repetição de rede não cria duas campanhas.
  const [key, setKey] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [local, setLocal] = useState<string | null>(null);

  async function check() {
    setBusy(true);
    setLocal(null);
    try {
      setPreview(await api.adminNotificationPreview(groupId, { quick: kind, audience: kind === "RegistrationReminder" ? audience : undefined }));
      setKey(newIdempotencyKey());
    } catch (e) {
      setLocal(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  async function send() {
    if (!key) return;
    setBusy(true);
    setLocal(null);
    onError(null);
    try {
      const c = await api.adminQuickReminder(groupId, { kind, audience: kind === "RegistrationReminder" ? audience : undefined }, key);
      onSent(`${title}: ${c.recipientCount} pessoa(s) no app, ${c.pushDeviceCount} aparelho(s) na fila de push.`);
      setPreview(null);
      setKey(null);
    } catch (e) {
      setLocal(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <AdminCard>
      <div className="flex flex-col gap-2">
        <p className="text-body-lg font-semibold">
          <span aria-hidden="true">{icon} </span>
          {title}
        </p>
        <p className="text-body-md text-on-surface-variant">{text}</p>
        {kind === "RegistrationReminder" ? (
          <label className="flex flex-col gap-1 text-label-md">
            Para quem
            <select
              className={inputClass}
              value={audience}
              onChange={(e) => {
                setAudience(e.target.value as "NotResponded" | "AllMembers");
                setPreview(null);
              }}
            >
              <option value="NotResponded">Quem ainda não respondeu</option>
              <option value="AllMembers">Todos os membros ativos</option>
            </select>
          </label>
        ) : null}
        {preview ? (
          <>
            <p role="status" className="text-body-md">
              Vai para <strong>{preview.recipients}</strong> pessoa(s); {preview.pushDevices} aparelho(s) com push.
            </p>
            {preview.recipients > 0 ? (
              <AdminConfirmButton
                tone="neutral"
                className={primaryButton}
                label={`Enviar para ${preview.recipients}`}
                question={`Enviar "${title}" para ${preview.recipients} pessoa(s)?`}
                confirmLabel="Enviar agora"
                disabled={busy}
                onConfirm={send}
              />
            ) : (
              <p className="text-body-md text-on-surface-variant">Ninguém nesse público agora.</p>
            )}
          </>
        ) : (
          <button type="button" disabled={busy} className={`${secondaryButton} self-start`} onClick={check}>
            {busy ? "Calculando…" : "Ver alcance"}
          </button>
        )}
        {local ? (
          <p role="alert" className="text-body-md text-on-error-container">
            {local}
          </p>
        ) : null}
      </div>
    </AdminCard>
  );
}

function Broadcast({ groupId, onSent }: { groupId: string; onSent: (msg: string) => void }) {
  const [audience, setAudience] = useState<BroadcastAudience>("AllMembers");
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [actionUrl, setActionUrl] = useState<NotificationActionUrl>("/partida");
  const [preview, setPreview] = useState<Preview | null>(null);
  const [key, setKey] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [local, setLocal] = useState<string | null>(null);
  const issues = broadcastIssues(title, body, actionUrl);

  // Qualquer mudança no texto invalida a prévia e a chave (o mesmo envio precisa do mesmo corpo).
  const changed = () => {
    setPreview(null);
    setKey(null);
  };

  async function check() {
    setBusy(true);
    setLocal(null);
    try {
      setPreview(await api.adminNotificationPreview(groupId, { audience }));
      setKey(newIdempotencyKey());
    } catch (e) {
      setLocal(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  async function send() {
    if (!key) return;
    setBusy(true);
    setLocal(null);
    try {
      const c = await api.adminBroadcast(groupId, { audience, title: title.trim(), body: body.trim(), actionUrl }, key);
      onSent(`Comunicado enviado: ${c.recipientCount} pessoa(s) no app, ${c.pushDeviceCount} aparelho(s) na fila de push.`);
      setTitle("");
      setBody("");
      changed();
    } catch (e) {
      setLocal(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <AdminCard title="Comunicado personalizado" titleId="comunicado-titulo" description="Texto puro, sem valores em reais ou dados pessoais: o aviso aparece na tela bloqueada.">
      <form
        className="flex flex-col gap-md"
        onSubmit={(e) => {
          e.preventDefault();
          if (issues.length === 0) check();
        }}
      >
        <label className="flex flex-col gap-1 text-label-md">
          Para quem
          <select
            className={inputClass}
            value={audience}
            onChange={(e) => {
              setAudience(e.target.value as BroadcastAudience);
              changed();
            }}
          >
            {BROADCAST_AUDIENCES.map((a) => (
              <option key={a} value={a}>
                {AUDIENCE_LABEL[a]}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-label-md">
          <span className="flex justify-between">
            Título <span className={title.length > TITLE_MAX ? "text-error" : "text-on-surface-variant"}>{title.length}/{TITLE_MAX}</span>
          </span>
          <input
            className={inputClass}
            value={title}
            maxLength={TITLE_MAX + 20}
            onChange={(e) => {
              setTitle(e.target.value);
              changed();
            }}
            placeholder="Ex.: Jogo confirmado mesmo com chuva"
          />
        </label>
        <label className="flex flex-col gap-1 text-label-md">
          <span className="flex justify-between">
            Mensagem <span className={body.length > BODY_MAX ? "text-error" : "text-on-surface-variant"}>{body.length}/{BODY_MAX}</span>
          </span>
          <textarea
            className={inputClass}
            rows={3}
            value={body}
            maxLength={BODY_MAX + 50}
            onChange={(e) => {
              setBody(e.target.value);
              changed();
            }}
          />
        </label>
        <label className="flex flex-col gap-1 text-label-md sm:max-w-xs">
          Ao tocar, abrir
          <select
            className={inputClass}
            value={actionUrl}
            onChange={(e) => {
              setActionUrl(e.target.value as NotificationActionUrl);
              changed();
            }}
          >
            {ACTION_URLS.map((a) => (
              <option key={a.url} value={a.url}>
                {a.label}
              </option>
            ))}
          </select>
        </label>

        {title.trim() || body.trim() ? (
          <div aria-label="Prévia do aviso" className="flex items-start gap-3 rounded-xl border border-outline-variant bg-surface-container-low p-sm">
            {/* eslint-disable-next-line @next/next/no-img-element -- ícone local estático */}
            <img src="/icons/icon-192.png" alt="" width={36} height={36} className="rounded-lg" />
            <div className="min-w-0">
              <p className="text-body-md font-semibold">{title || "Título"}</p>
              <p className="whitespace-pre-line break-words text-body-md text-on-surface-variant">{body || "Mensagem"}</p>
            </div>
          </div>
        ) : null}

        {issues.length > 0 && (title || body) ? (
          <ul className="text-label-md text-on-error-container">
            {issues.map((i) => (
              <li key={i}>! {i}</li>
            ))}
          </ul>
        ) : null}

        {!preview ? (
          <button type="submit" disabled={busy || issues.length > 0} className={`${secondaryButton} self-start`}>
            {busy ? "Calculando…" : "Pré-visualizar alcance"}
          </button>
        ) : (
          <div className="flex flex-col gap-2">
            <p role="status" className="text-body-md">
              Vai para <strong>{preview.recipients}</strong> pessoa(s); {preview.pushDevices} aparelho(s) com push.
            </p>
            {preview.recipients > 0 ? (
              <AdminConfirmButton
                tone="neutral"
                className={primaryButton}
                label={`Enviar para ${preview.recipients} pessoa(s)`}
                question="Confirmar o envio do comunicado?"
                confirmLabel="Enviar comunicado"
                disabled={busy}
                onConfirm={send}
              />
            ) : (
              <p className="text-body-md text-on-surface-variant">Ninguém nesse público agora.</p>
            )}
          </div>
        )}
        {local ? (
          <p role="alert" className="text-body-md text-on-error-container">
            {local}
          </p>
        ) : null}
      </form>
    </AdminCard>
  );
}
