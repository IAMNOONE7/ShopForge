import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { platformApi, type PlatformTenant, type TenantAction } from "../api/platform";
import { isHttpError } from "../api/http";
import { useRequest } from "../useRequest";
import { useAction } from "../useAction";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { formatNumber } from "../utils/format";

type Confirmation = { item: PlatformTenant; action: TenantAction };
type Outcome = { kind: "acknowledged" | "refused" | "uncertain"; name: string; action: TenantAction; error: unknown | null };
const usageNames = ["stores", "products", "listings", "orders", "customers", "users"] as const;
const pageSize = 25;
export function TenantsPage({ userId, sessionStale }: { userId: string; sessionStale: boolean }) {
  const { t, i18n } = useTranslation(["operator", "common"]);
  const [state, reload] = useRequest(`platform-tenants:${userId}`, signal => platformApi.tenants(signal));
  const [draft, setDraft] = useState(""), [query, setQuery] = useState(""), [status, setStatus] = useState("all"), [page, setPage] = useState(1);
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null), [outcome, setOutcome] = useState<Outcome | null>(null);
  const [invalidated, setInvalidated] = useState<PlatformTenant[] | null>(null);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(true), controller = useRef<AbortController | null>(null), heading = useRef<HTMLHeadingElement>(null), result = useRef<HTMLDivElement>(null), opener = useRef<HTMLButtonElement | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (outcome) result.current?.focus(); }, [outcome]);
  const items = state.status === "ready" ? state.data : [];
  const ready = state.status === "ready" && !state.refreshing && state.refreshError === null && state.data !== invalidated;
  const disabled = !ready || sessionStale || pending || outcome?.kind === "uncertain";
  const current = confirmation ? items.find(item => item.id === confirmation.item.id) : null;
  const stale = confirmation !== null && (!current || current.name !== confirmation.item.name || current.status !== confirmation.item.status);
  const matched = items.filter(item => (status === "all" || item.status === status) && (!query || `${item.name} ${item.id}`.toLocaleLowerCase().includes(query.toLocaleLowerCase())));
  const pages = Math.max(1, Math.ceil(matched.length / pageSize)), currentPage = Math.min(page, pages), shown = matched.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const readError = state.status === "error" || state.status === "not-found" ? state.error : state.status === "ready" ? state.refreshError : null;
  function close() { if (pending) return; setConfirmation(null); window.requestAnimationFrame(() => { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); }); }
  function open(item: PlatformTenant, button: HTMLButtonElement) {
    if (disabled || confirmation || !["Active", "Suspended"].includes(item.status)) return;
    opener.current = button; setOutcome(null); setConfirmation({ item, action: item.status === "Active" ? "suspend" : "resume" });
  }
  async function save() {
    if (disabled || !confirmation || stale || state.status !== "ready") return;
    const intent = confirmation;
    await run(async () => {
      setInvalidated(state.data); setOutcome(null); controller.current = new AbortController();
      try {
        const saved = await platformApi.changeTenant(intent.item.id, intent.action, controller.current.signal);
        if (active.current) { setConfirmation(null); setOutcome({ kind: "acknowledged", name: saved.name, action: intent.action, error: null }); }
      } catch (error) {
        if (!active.current) return;
        const refused = isHttpError(error) && error.kind === "http" && error.status !== null && [400, 401, 403, 404, 409, 422, 429].includes(error.status);
        if (!refused) setConfirmation(null);
        setOutcome({ kind: refused ? "refused" : "uncertain", name: intent.item.name, action: intent.action, error });
      } finally { if (active.current) reload(); }
    });
  }
  return <div className="operator-workspace"><div className="section-heading"><div><h1 ref={heading} tabIndex={-1}>{t("companies")}</h1><p>{t("companiesHint")}</p></div>
    <Button type="button" variant="secondary" disabled={pending || state.status === "ready" && state.refreshing} onClick={reload}>{t("refresh")}</Button></div>
    {state.status === "loading" && <LoadingState label={t("loading")} lines={3} />}
    {readError !== null && <RequestError error={readError} operation="read" onRetry={reload} />}
    {!ready && state.status === "ready" && <InlineMessage>{t("staleRead")}</InlineMessage>}
    {sessionStale && <InlineMessage>{t("sessionStale")}</InlineMessage>}
    {outcome && <div className="operator-result" ref={result} tabIndex={-1}>
      {outcome.error !== null && <RequestError error={outcome.error} operation="write" />}
      <InlineMessage tone={outcome.kind === "acknowledged" ? "success" : outcome.kind === "uncertain" ? "error" : "info"}>{t(outcome.kind, { name: outcome.name })}</InlineMessage>
      {outcome.kind === "uncertain" && <Button type="button" disabled={!ready || pending} onClick={() => { if (ready && !pending) { setOutcome(null); heading.current?.focus(); } }}>{t("finishReview")}</Button>}
    </div>}
    <form className="operator-filters" onSubmit={event => { event.preventDefault(); setQuery(draft.trim()); setPage(1); }}>
      <Field id="operator-query" label={t("search")} value={draft} onChange={event => setDraft(event.target.value)} />
      <div className="listing-field"><label htmlFor="operator-status">{t("statusFilter")}</label><select id="operator-status" value={status} onChange={event => { setStatus(event.target.value); setPage(1); }}>
        <option value="all">{t("allStatuses")}</option><option value="Active">{t("status.Active")}</option><option value="Suspended">{t("status.Suspended")}</option></select></div>
      <Button type="submit" variant="secondary">{t("searchAction")}</Button>
    </form>
    {state.status === "ready" && <><p role="status">{t("count", { shown: matched.length, total: items.length })}</p>
      {!items.length ? <p>{t("empty")}</p> : !matched.length ? <p>{t("noMatches")}</p> : <ul className="operator-tenants">{shown.map(item => {
        const known = item.status === "Active" || item.status === "Suspended";
        return <li key={item.id}><div className="section-heading"><h2>{item.name}</h2><span className="chip">{t(item.status === "Active" ? "status.Active" : item.status === "Suspended" ? "status.Suspended" : "status.unknown")}</span></div>
          <dl className="operator-facts"><div><dt>{t("companyId")}</dt><dd><code>{item.id}</code></dd></div><div><dt>{t("plan")}</dt><dd>{item.planCode ?? t("noPlan")}</dd></div></dl>
          {!known && <p>{t("unknownStatus", { status: item.status })}</p>}
          <details><summary>{t("usageTitle")}</summary>{item.usage.length ? <dl className="operator-usage">{item.usage.map(usage => {
            const key = usageNames.find(name => name === usage.name);
            return <div key={usage.name}><dt>{key ? t(`usage.${key}`) : usage.name}</dt><dd>{formatNumber(usage.value, i18n.resolvedLanguage)}</dd></div>;
          })}</dl> : <p>{t("noUsage")}</p>}</details>
          {known && <Button type="button" variant={item.status === "Active" ? "danger" : "secondary"} disabled={disabled || confirmation !== null}
            aria-label={t(item.status === "Active" ? "suspendNamed" : "resumeNamed", { name: item.name })} onClick={event => open(item, event.currentTarget)}>{t(item.status === "Active" ? "suspend" : "resume")}</Button>}
        </li>;
      })}</ul>}
      {matched.length > pageSize && <nav className="operator-paging" aria-label={t("paging")}><Button type="button" variant="secondary" disabled={currentPage === 1} onClick={() => setPage(Math.max(1, currentPage - 1))}>{t("previous")}</Button>
        <p>{t("page", { page: currentPage, total: pages })}</p><Button type="button" variant="secondary" disabled={currentPage === pages} onClick={() => setPage(Math.min(pages, currentPage + 1))}>{t("next")}</Button></nav>}
    </>}
    {confirmation && <TenantConfirmation intent={confirmation} pending={pending} disabled={disabled || stale} stale={stale} onConfirm={save} onCancel={close} />}
  </div>;
}
function TenantConfirmation({ intent, disabled, pending, stale, onConfirm, onCancel }: { intent: Confirmation; disabled: boolean; pending: boolean; stale: boolean; onConfirm: () => Promise<void>; onCancel: () => void }) {
  const { t } = useTranslation("operator"), heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  return <section className="operator-confirmation" aria-labelledby="operator-confirm-title" onKeyDown={event => { if (event.key === "Escape" && !pending) { event.preventDefault(); onCancel(); } }}>
    <h2 id="operator-confirm-title" ref={heading} tabIndex={-1}>{t(intent.action === "suspend" ? "confirmSuspendTitle" : "confirmResumeTitle", { name: intent.item.name })}</h2>
    <p><code>{intent.item.id}</code></p><p>{t(intent.action === "suspend" ? "suspendEffect" : "resumeEffect")}</p>
    {stale && <InlineMessage>{t("confirmationStale")}</InlineMessage>}
    <div className="cluster"><Button type="button" variant={intent.action === "suspend" ? "danger" : "primary"} disabled={disabled} busy={pending} busyLabel={t("changing")} onClick={() => void onConfirm()}>{t(intent.action === "suspend" ? "confirmSuspend" : "confirmResume")}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button></div>
  </section>;
}
