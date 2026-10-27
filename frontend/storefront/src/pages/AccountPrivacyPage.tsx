import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import type { Customer } from "../account";
import { customerEmailApi } from "../api/customerEmail";
import { privacyApi, type PrivacySummary } from "../api/privacy";
import { isHttpError, saveDownload } from "../api/http";
import { statusOf } from "../api/errors";
import { authPath, clearAuthReturn } from "../auth";
import { AuthLayout } from "../components/auth/AuthLayout";
import { PasswordField } from "../components/auth/PasswordField";
import { Button } from "../components/ui/Button";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useCart } from "../cartContext";
import { useCustomer } from "../customerContext";
import { useStore } from "../storeContext";
import "./AccountPrivacyPage.css";

type DeletionOutcome = { kind: "deleted" } | { kind: "uncertain"; error: unknown };
const sectionNames = new Map<string, "sectionAccount" | "sectionWishlist" | "sectionConsents" | "sectionOrders" | "sectionReturns" | "sectionInvoices" | "sectionReviews">([
  ["account", "sectionAccount"], ["wishlist", "sectionWishlist"], ["consents", "sectionConsents"], ["orders", "sectionOrders"],
  ["returns", "sectionReturns"], ["invoices", "sectionInvoices"], ["reviews", "sectionReviews"],
] as const);
export function AccountPrivacyPage() {
  const store = useStore();
  return <PrivacyPage key={store.id} />;
}
function PrivacyPage() {
  const session = useCustomer(), cart = useCart(), { t } = useTranslation("privacy");
  const [outcome, setOutcome] = useState<DeletionOutcome | null>(null), [changed, setChanged] = useState(false);
  function forget(result: DeletionOutcome) {
    setOutcome(result); clearAuthReturn(); session.apply(null); cart.reload();
  }
  if (outcome) return <PrivacyOutcome outcome={outcome} />;
  if (session.status === "checking") return <AuthLayout className="privacy-page" title={t("title")}><LoadingState label={t("checking")} /></AuthLayout>;
  if (session.status === "error") return <AuthLayout className="privacy-page" title={t("title")}><RequestError error={session.error} operation="session" onRetry={session.retry} /></AuthLayout>;
  if (session.status !== "authenticated" || !session.customer) return <AuthLayout className="privacy-page" title={t("title")}><p>{t("signInRequired")}</p><Link to={authPath("/account/sign-in", "/account/privacy")}>{t("signIn")}</Link></AuthLayout>;
  return <PrivacyWorkspace key={session.customer.email} customer={session.customer} changed={changed} onDifferent={customer => { setChanged(true); session.apply(customer); }} onExpired={() => { clearAuthReturn(); session.apply(null); cart.reload(); }} onDeleted={forget} />;
}
function PrivacyWorkspace({ customer, changed, onDifferent, onExpired, onDeleted }: {
  customer: Customer; changed: boolean; onDifferent: (customer: Customer) => void; onExpired: () => void; onDeleted: (outcome: DeletionOutcome) => void;
}) {
  const { t, i18n } = useTranslation("privacy"), store = useStore();
  const [pending, setPending] = useState<"export" | "delete" | null>(null), [editing, setEditing] = useState(false), [confirmed, setConfirmed] = useState(false), [attempt, setAttempt] = useState(0);
  const [issues, setIssues] = useState<("password" | "confirm")[]>([]), [failure, setFailure] = useState<{ error: unknown; operation: "read" | "write" } | null>(null), [summary, setSummary] = useState<PrivacySummary | null>(null), [cancelled, setCancelled] = useState(false);
  const active = useRef(false), lock = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null), editor = useRef<HTMLFormElement>(null), deleteSection = useRef<HTMLElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (failure || summary || cancelled || issues.length) feedback.current?.focus(); }, [failure, summary, cancelled, issues]);
  useEffect(() => { if (editing) editor.current?.querySelector<HTMLInputElement>('input[name="password"]')?.focus(); }, [editing]);
  const owns = (request: AbortController) => active.current && !request.signal.aborted;
  async function checkSession(request: AbortController) {
    const current = await customerEmailApi.profile(request.signal);
    if (!owns(request)) return false;
    if (current.email !== customer.email) { onDifferent(current); return false; }
    return true;
  }
  function close() {
    if (lock.current) return;
    setEditing(false); setConfirmed(false); setIssues([]); setFailure(null);
    requestAnimationFrame(() => { if (active.current) deleteSection.current?.querySelector<HTMLButtonElement>("button")?.focus(); });
  }
  async function download() {
    if (lock.current || editing) return;
    lock.current = true; setPending("export"); setFailure(null); setSummary(null); setCancelled(false);
    const request = new AbortController(); controller.current = request;
    try {
      if (!await checkSession(request)) return;
      const file = await privacyApi.export(customer.email, request.signal);
      if (!owns(request) || !await checkSession(request)) return;
      saveDownload(file); setSummary({ exportedAt: file.exportedAt, sections: file.sections });
    } catch (error) {
      if (!owns(request)) return;
      if (statusOf(error) === 401) { onExpired(); return; }
      setFailure({ error, operation: "read" });
    } finally {
      lock.current = false;
      if (active.current) { setPending(null); if (request.signal.aborted) setCancelled(true); }
    }
  }
  async function erase(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (lock.current || !editing) return;
    const password = String(new FormData(event.currentTarget).get("password") ?? "");
    const invalid: ("password" | "confirm")[] = [...(!password ? ["password" as const] : []), ...(!confirmed ? ["confirm" as const] : [])];
    setIssues(invalid); setFailure(null); if (invalid.length) return;
    const field = event.currentTarget.elements.namedItem("password"); if (field instanceof HTMLInputElement) field.value = "";
    setAttempt(value => value + 1); lock.current = true; setPending("delete"); setCancelled(false);
    const request = new AbortController(); controller.current = request; let submitted = false;
    try {
      if (!await checkSession(request)) return;
      submitted = true; await privacyApi.erase(password, request.signal);
      if (owns(request)) onDeleted({ kind: "deleted" });
    } catch (error) {
      if (!owns(request)) return;
      if (statusOf(error) === 401) { onExpired(); return; }
      const refused = isHttpError(error) && error.kind === "http" && [400, 403, 404, 429].includes(error.status ?? 0);
      if (submitted && !refused) { onDeleted({ kind: "uncertain", error }); return; }
      setFailure({ error, operation: submitted ? "write" : "read" });
    } finally { lock.current = false; if (active.current) setPending(null); }
  }
  const wrongPassword = failure?.operation === "write" && statusOf(failure.error) === 403;
  const message = (failure || summary || cancelled || issues.length > 0) && <div ref={feedback} tabIndex={-1} className="privacy-feedback">
        {issues.length > 0 && <div role="alert"><p>{t("checkFields")}</p><ul>{issues.map(issue => <li key={issue}><button type="button" className="link-button" onClick={() => editor.current?.querySelector<HTMLElement>(`[name="${issue}"]`)?.focus()}>{t(issue === "password" ? "passwordRequired" : "confirmationRequired")}</button></li>)}</ul></div>}
        {wrongPassword ? <p role="alert">{t("wrongPassword")}</p> : failure && <><RequestError error={failure.error} operation={failure.operation} />{failure.operation === "read" && editing && <p>{t("notSubmitted")}</p>}</>}
        {cancelled && <p role="status">{t("downloadCancelled")}</p>}
        {summary && <><p role="status">{t("downloadReady")}</p><p>{t("exportedAt")}: <time dateTime={summary.exportedAt}>{new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: "medium", timeStyle: "short" }).format(new Date(summary.exportedAt))}</time></p>
          <details><summary>{t("contents")}</summary><dl className="privacy-sections">{summary.sections.map(section => <div key={section.name}><dt>{sectionNames.has(section.name) ? t(sectionNames.get(section.name)!) : section.name}</dt><dd>{new Intl.NumberFormat(i18n.resolvedLanguage).format(section.entries)}</dd></div>)}</dl></details></>}
      </div>;
  return <AuthLayout className="privacy-page" title={t("title")} introduction={<p>{t("intro")}</p>} footer={<Link to="/account">{t("backAccount")}</Link>}>
    <div className="privacy-workspace"><p className="privacy-target">{t("target", { email: customer.email, store: store.name })}</p>
      {changed && <p role="status">{t("accountChanged")}</p>}
      <section aria-labelledby="privacy-export-heading"><h2 id="privacy-export-heading">{t("exportTitle")}</h2><p>{t("exportBody")}</p><p className="hint">{t("exportPrivate")}</p>
        <Button type="button" disabled={editing || pending !== null} busy={pending === "export"} busyLabel={t("exporting")} onClick={() => void download()}>{t("download")}</Button>
        {pending === "export" && <Button type="button" variant="secondary" onClick={() => controller.current?.abort()}>{t("cancelDownload")}</Button>}
        {!editing && message}
      </section>
      <section ref={deleteSection} className="privacy-delete" aria-labelledby="privacy-delete-heading"><h2 id="privacy-delete-heading">{t("deleteTitle")}</h2><p>{t("deleteBody")}</p><p>{t("retainedRecords")}</p>
        {!editing ? <Button type="button" variant="danger" disabled={pending !== null} onClick={() => { if (lock.current) return; setEditing(true); setSummary(null); setFailure(null); setIssues([]); setCancelled(false); }}>{t("beginDelete")}</Button> : <form ref={editor} aria-label={t("deleteConfirmation")} noValidate aria-busy={pending === "delete" || undefined} onSubmit={event => void erase(event)} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); close(); } }}>
          <p>{t("target", { email: customer.email, store: store.name })}</p>
          <PasswordField key={attempt} name="password" autoComplete="current-password" label={t("currentPassword")} hint={t("passwordHint")} disabled={pending !== null} invalid={Boolean(wrongPassword || issues.includes("password"))} />
          <label className="privacy-confirm"><input id="privacy-confirm" name="confirm" type="checkbox" checked={confirmed} disabled={pending !== null} aria-invalid={issues.includes("confirm") || undefined} onChange={event => { setConfirmed(event.target.checked); setIssues(current => current.filter(issue => issue !== "confirm")); }} />{t("acknowledge")}</label>
          <div className="privacy-actions"><Button type="submit" variant="danger" busy={pending === "delete"} busyLabel={t("deleting")} disabled={pending !== null}>{t("confirmDelete")}</Button><Button type="button" variant="secondary" disabled={pending !== null} onClick={close}>{t("cancelDelete")}</Button></div>
        </form>}
        {editing && message}
      </section>

    </div>
  </AuthLayout>;
}
function PrivacyOutcome({ outcome }: { outcome: DeletionOutcome }) {
  const { t } = useTranslation("privacy"), session = useCustomer();
  const [pending, setPending] = useState(false), [error, setError] = useState<unknown | null>(null), [checked, setChecked] = useState<{ customer: Customer | null } | null>(null);
  const active = useRef(false), lock = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null);
  useEffect(() => { active.current = true; feedback.current?.focus(); return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (checked || error) feedback.current?.focus(); }, [checked, error]);
  async function check() {
    if (lock.current || outcome.kind !== "uncertain") return;
    lock.current = true; setPending(true); setError(null); const request = new AbortController(); controller.current = request;
    try {
      let customer: Customer | null;
      try { customer = await customerEmailApi.profile(request.signal); } catch (failure) { if (statusOf(failure) !== 401) throw failure; customer = null; }
      if (active.current && !request.signal.aborted) { setChecked({ customer }); session.apply(customer); }
    } catch (failure) { if (active.current && !request.signal.aborted) setError(failure); }
    finally { lock.current = false; if (active.current) setPending(false); }
  }
  return <AuthLayout className="privacy-page" title={t(outcome.kind === "deleted" ? "deletedTitle" : "uncertainTitle")} footer={<Link to="/">{t("continueShopping")}</Link>}>
    <div ref={feedback} tabIndex={-1} className="privacy-workspace"><p role="status">{t(outcome.kind === "deleted" ? "deletedBody" : "uncertainBody")}</p>
      {outcome.kind === "uncertain" && <><RequestError error={outcome.error} operation="write" /><Button type="button" busy={pending} busyLabel={t("checking")} onClick={() => void check()}>{t("checkSession")}</Button>
        {error !== null && <RequestError error={error} operation="read" />}
        {checked && <><p role="status">{checked.customer ? t("currentSession", { email: checked.customer.email }) : t("guestSession")}</p><p>{t("notProof")}</p><Link to="/account">{t("backAccount")}</Link></>}
      </>}
    </div>
  </AuthLayout>;
}
