import { useEffect, useEffectEvent, useRef, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useAdminOutlet } from "../adminContext";
import { isHttpError } from "../api/http";
import { sameStaffAccount } from "../api/staffAccount";
import { tenantAuditApi, type AuditRecord } from "../api/tenantAudit";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { auditParameters, auditQuery, checkedAuditDraft, maxAuditPage, type AuditDraft, type AuditIssue, type AuditQuery } from "../utils/auditQuery";
import { formatNumber, uiLocale } from "../utils/format";
import { ForbiddenPage } from "./RouteStatePage";
import "./TenantAuditPage.css";

type Closed = "expired" | "changed" | "forbidden";
type View = { kind: "loading" } | { kind: "ready"; rows: AuditRecord[] } | { kind: "failed"; error: unknown };
class SessionChanged extends Error {}
const fieldId = (issue: AuditIssue) => `audit-filter-${issue === "range" ? "to" : issue === "page" ? "pageSize" : issue}`;

function AuditFilters({ initial, initialIssues, onApply, onReset }: { initial: AuditDraft; initialIssues: AuditIssue[]; onApply: (query: AuditQuery) => void; onReset: () => void }) {
  const { t } = useTranslation("tenantAudit");
  const { stores } = useAdminOutlet();
  const [draft, setDraft] = useState(initial);
  const [issues, setIssues] = useState(initialIssues);
  const feedback = useRef<HTMLDivElement>(null);
  const knownStores = stores.status === "ready" ? stores.data : [];
  const unknownStore = draft.storeId && !knownStores.some(store => store.id.toLowerCase() === draft.storeId.toLowerCase());
  const sizes = ["10", "25", "50", "100"];
  useEffect(() => { if (issues.length) feedback.current?.focus(); }, [issues]);
  function update(key: keyof AuditDraft, value: string) { setDraft({ ...draft, [key]: value }); setIssues(issues.filter(issue => issue !== key && !(issue === "range" && (key === "from" || key === "to")))); }
  function submit(event: FormEvent) {
    event.preventDefault();
    const checked = checkedAuditDraft(draft); setIssues(checked.issues);
    if (checked.query) onApply(checked.query);
  }
  return <form className="tenant-audit-filters" aria-label={t("filters")} noValidate onSubmit={submit}>
    {issues.length > 0 && <div ref={feedback} className="tenant-audit-feedback" tabIndex={-1} role="alert"><p>{t("checkFields")}</p><ul>{issues.map(issue => <li key={issue}><button type="button" className="link-button" onClick={() => document.getElementById(fieldId(issue))?.focus()}>{t(`validation.${issue}`)}</button></li>)}</ul></div>}
    <div className="tenant-audit-filter-grid">
      <div className="tenant-audit-native-field"><label htmlFor="audit-filter-storeId">{t("storeFilter")}</label><select id="audit-filter-storeId" value={draft.storeId} aria-invalid={issues.includes("storeId") || undefined} aria-describedby="audit-store-hint" onChange={event => update("storeId", event.target.value)}><option value="">{t("allStores")}</option>{unknownStore && <option value={draft.storeId}>{t("storeFromLink", { id: draft.storeId })}</option>}{knownStores.map(store => <option key={store.id} value={store.id.toLowerCase() === draft.storeId.toLowerCase() ? draft.storeId : store.id} lang={store.culture}>{store.name}</option>)}</select></div>
      <Field id="audit-filter-action" label={t("actionFilter")} name="action" value={draft.action} maxLength={60} autoComplete="off" spellCheck={false} hint={t("actionHint")} error={issues.includes("action") ? t("validation.action") : undefined} onChange={event => update("action", event.target.value)} />
      <Field id="audit-filter-from" label={t("from")} name="from" value={draft.from} autoComplete="off" spellCheck={false} placeholder="2026-10-01T08:30:00Z" aria-describedby="audit-time-hint" error={issues.includes("from") ? t("validation.from") : undefined} onChange={event => update("from", event.target.value)} />
      <Field id="audit-filter-to" label={t("to")} name="to" value={draft.to} autoComplete="off" spellCheck={false} placeholder="2026-10-02T08:30:00Z" aria-describedby="audit-time-hint" error={issues.includes("to") || issues.includes("range") ? t(issues.includes("range") ? "validation.range" : "validation.to") : undefined} onChange={event => update("to", event.target.value)} />
    </div>
    <p id="audit-store-hint" className="hint">{t("storeHint")}</p>
    {stores.status !== "ready" && <p className="hint">{t("storeNamesUnavailable")}</p>}
    {unknownStore && <p className="hint">{t("storeFromLink", { id: draft.storeId })}</p>}
    <p id="audit-time-hint" className="hint">{t("timeHint")}</p>
    <div className="tenant-audit-native-field tenant-audit-size"><label htmlFor="audit-filter-pageSize">{t("pageSize")}</label><select id="audit-filter-pageSize" value={draft.pageSize} aria-invalid={issues.includes("pageSize") || undefined} onChange={event => update("pageSize", event.target.value)}>{!sizes.includes(draft.pageSize) && <option value={draft.pageSize}>{draft.pageSize || t("invalidSize")}</option>}{sizes.map(size => <option key={size} value={size}>{size}</option>)}</select></div>
    <div className="tenant-audit-actions"><Button type="submit">{t("apply")}</Button><Button type="button" variant="secondary" onClick={() => { setDraft({ storeId: "", action: "", from: "", to: "", pageSize: "50" }); setIssues([]); onReset(); }}>{t("clear")}</Button></div>
  </form>;
}

function AuditRecordCard({ row }: { row: AuditRecord }) {
  const { t, i18n } = useTranslation("tenantAudit");
  const knownKind = (["TenantUser", "PlatformUser", "Customer", "System"] as const).find(kind => kind === row.actorKind);
  const supplied = (value: string | null) => value === null ? t("notRecorded") : value || t("emptyValue");
  return <li className="tenant-audit-record"><article aria-labelledby={`audit-entry-${row.id}`}>
    <header className="tenant-audit-record-heading"><h3 id={`audit-entry-${row.id}`}><code>{row.action}</code></h3><time dateTime={row.recordedAt}>{new Intl.DateTimeFormat(uiLocale(i18n.resolvedLanguage), { dateStyle: "medium", timeStyle: "long", timeZone: "UTC" }).format(new Date(row.recordedAt))}</time></header>
    <dl className="tenant-audit-facts"><div><dt>{t("subject")}</dt><dd>{row.subject}</dd></div><div><dt>{t("actorName")}</dt><dd>{supplied(row.actorName)}</dd></div><div><dt>{t("actorKind")}</dt><dd>{knownKind ? t(`actorKinds.${knownKind}`) : <code>{row.actorKind}</code>}</dd></div><div><dt>{t("storeId")}</dt><dd>{row.storeId === null ? t("noStore") : <code>{row.storeId}</code>}</dd></div></dl>
    <details><summary>{t("details")}</summary><dl className="tenant-audit-facts"><div><dt>{t("recordId")}</dt><dd><code>{row.id}</code></dd></div><div><dt>{t("actorId")}</dt><dd>{supplied(row.actorId)}</dd></div><div><dt>{t("ipAddress")}</dt><dd>{supplied(row.ipAddress)}</dd></div><div><dt>{t("recordedTimestamp")}</dt><dd><code>{row.recordedAt}</code></dd></div></dl><h4>{t("recordedDetails")}</h4>{row.details === null ? <p>{t("noDetails")}</p> : <pre>{row.details || t("emptyValue")}</pre>}</details>
  </article></li>;
}

export function TenantAuditPage() {
  const { user } = useSession();
  if (!canManageStore(user.role)) return <ForbiddenPage />;
  return <TenantAuditWorkspace key={JSON.stringify([user.id, user.tenantId, user.role, user.email.trim().toLowerCase()])} />;
}

function TenantAuditWorkspace() {
  const { user } = useSession();
  const { t, i18n } = useTranslation("tenantAudit");
  const [parameters, setParameters] = useSearchParams();
  const parsed = auditQuery(parameters);
  const query = parsed.query;
  const key = query ? auditParameters(query).toString() : `invalid:${parameters}`;
  const [result, setResult] = useState<{ key: string; view: View } | null>(null);
  const [closed, setClosed] = useState<Closed | null>(null);
  const active = useRef(false);
  const controller = useRef<AbortController | null>(null);
  const feedback = useRef<HTMLDivElement>(null);
  const summary = useRef<HTMLParagraphElement>(null);
  const focusResults = useRef(false);
  const owns = (request: AbortController) => active.current && controller.current === request && !request.signal.aborted;
  const view: View = result?.key === key ? result.view : { kind: "loading" };
  async function checkSession(request: AbortController) {
    const actual = await tenantAuditApi.session(request.signal);
    if (!owns(request)) return false;
    if (!sameStaffAccount(user, actual)) throw new SessionChanged();
    return true;
  }
  async function snapshot(chosen: AuditQuery, request: AbortController) {
    if (!await checkSession(request)) return null;
    const rows = await tenantAuditApi.read(chosen, request.signal);
    if (!owns(request) || !await checkSession(request)) return null;
    return rows;
  }
  function load(chosen: AuditQuery, request: AbortController) {
    void snapshot(chosen, request).then(rows => { if (rows && owns(request)) setResult({ key, view: { kind: "ready", rows } }); })
      .catch(error => {
        if (!owns(request)) return;
        if (error instanceof SessionChanged) setClosed("changed");
        else if (isHttpError(error) && error.kind === "http" && (error.status === 401 || error.status === 403)) setClosed(error.status === 401 ? "expired" : "forbidden");
        else setResult({ key, view: { kind: "failed", error } });
      }).finally(() => { if (controller.current === request) controller.current = null; });
  }
  const loadInitially = useEffectEvent((request: AbortController) => { if (query) load(query, request); });
  useEffect(() => {
    active.current = true;
    if (query && !closed) { const request = new AbortController(); controller.current = request; loadInitially(request); }
    return () => { active.current = false; controller.current?.abort(); controller.current = null; };
  }, [key, closed]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (closed || parsed.invalidUrl || view.kind === "failed") feedback.current?.focus(); }, [closed, parsed.invalidUrl, view.kind]);
  useEffect(() => { if (result?.key === key && result.view.kind === "ready" && focusResults.current) { focusResults.current = false; summary.current?.focus(); } }, [result, key]);
  function refresh() {
    if (!query || closed || controller.current) return;
    const request = new AbortController(); controller.current = request; setResult({ key, view: { kind: "loading" } }); focusResults.current = true; load(query, request);
  }
  function navigate(next: AuditQuery) {
    if (closed) return;
    focusResults.current = true;
    const nextParameters = auditParameters(next);
    if (parameters.toString() === nextParameters.toString()) refresh(); else setParameters(nextParameters);
  }
  const rows = !closed && query && view.kind === "ready" ? view.rows : null;
  return <div className="tenant-audit-page"><div className="tenant-audit-heading"><h1>{t("title")}</h1><Button type="button" variant="secondary" disabled={closed !== null || !query || view.kind === "loading"} onClick={refresh}>{t("refresh")}</Button></div><p>{t("scope")}</p><p className="hint">{t("historyHint")}</p>
    <div ref={feedback} tabIndex={-1} className="tenant-audit-feedback">
      {closed && <><p role="alert">{t(closed)}</p><a href={`${import.meta.env.BASE_URL}audit`}>{t("reload")}</a></>}
      {!closed && parsed.invalidUrl && <p role="alert">{t("invalidUrl")}</p>}
      {!closed && query && view.kind === "failed" && <RequestError error={view.error} operation="read" onRetry={refresh} />}
    </div>
    {!closed && <AuditFilters key={parameters.toString()} initial={parsed.draft} initialIssues={parsed.issues} onApply={navigate} onReset={() => navigate({ storeId: "", action: "", from: "", to: "", page: 1, pageSize: 50 })} />}
    {!closed && query && view.kind === "loading" && <LoadingState label={t("loading")} />}
    {rows && query && <section className="tenant-audit-results" aria-labelledby="audit-results-title"><h2 id="audit-results-title">{t("results")}</h2><p ref={summary} tabIndex={-1} role="status">{t("pageSummary", { page: formatNumber(query.page, i18n.resolvedLanguage), amount: formatNumber(rows.length, i18n.resolvedLanguage) })}</p><p className="hint">{t("pagingHint")}</p>
      {rows.length === 0 ? <p>{t(query.page > 1 ? "emptyPage" : "empty")}</p> : <ul className="tenant-audit-records">{rows.map(row => <AuditRecordCard key={row.id} row={row} />)}</ul>}
      <nav className="tenant-audit-actions" aria-label={t("pages")}><Button type="button" variant="secondary" disabled={query.page === 1} onClick={() => navigate({ ...query, page: 1 })}>{t("first")}</Button><Button type="button" variant="secondary" disabled={query.page === 1} onClick={() => navigate({ ...query, page: query.page - 1 })}>{t("previous")}</Button><Button type="button" variant="secondary" disabled={rows.length < query.pageSize || query.page >= maxAuditPage(query.pageSize)} onClick={() => navigate({ ...query, page: query.page + 1 })}>{t("next")}</Button></nav>
      {query.page >= maxAuditPage(query.pageSize) && <p>{t("pageBound")}</p>}
    </section>}
  </div>;
}
