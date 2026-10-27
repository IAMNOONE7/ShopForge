import { useEffect, useEffectEvent, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import type { Customer } from "../account";
import { consentsApi, type Consent } from "../api/consents";
import { customerEmailApi } from "../api/customerEmail";
import { statusOf } from "../api/errors";
import { isHttpError } from "../api/http";
import { authPath } from "../auth";
import { AuthLayout } from "../components/auth/AuthLayout";
import { Button } from "../components/ui/Button";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";
import { useStore } from "../storeContext";
import "./AccountConsentsPage.css";

type View = { kind: "loading" } | { kind: "failed" | "uncertain"; error: unknown } | { kind: "ready"; rows: Consent[] };
type Notice = "saved" | "changed" | "reviewed" | "refreshed" | null;
const initialChoice = (row: Consent | undefined) => row?.decidedAt ? row.isGranted : null;
const sameConsent = (a: Consent, b: Consent) => a.purpose === b.purpose && a.isGranted === b.isGranted && a.statement === b.statement && a.decidedAt === b.decidedAt;

export function AccountConsentsPage() {
  const store = useStore();
  return <ConsentsPage key={store.id} />;
}
function ConsentsPage() {
  const session = useCustomer(), { t } = useTranslation("consents");
  const [changed, setChanged] = useState(false);
  if (session.status === "checking") return <AuthLayout className="consents-page" title={t("title")}><LoadingState label={t("checking")} /></AuthLayout>;
  if (session.status === "error") return <AuthLayout className="consents-page" title={t("title")}><RequestError error={session.error} operation="session" onRetry={session.retry} /></AuthLayout>;
  if (session.status !== "authenticated" || !session.customer) return <AuthLayout className="consents-page" title={t("title")}><p>{t("signInRequired")}</p><Link to={authPath("/account/sign-in", "/account/consents")}>{t("signIn")}</Link></AuthLayout>;
  return <ConsentWorkspace key={session.customer.email} customer={session.customer} changed={changed} onDifferent={customer => { setChanged(true); session.apply(customer); }} onExpired={() => session.apply(null)} />;
}
function ConsentWorkspace({ customer, changed, onDifferent, onExpired }: {
  customer: Customer; changed: boolean; onDifferent: (customer: Customer) => void; onExpired: () => void;
}) {
  const { t } = useTranslation("consents"), store = useStore();
  const [view, setView] = useState<View>({ kind: "loading" }), [choice, setChoice] = useState<boolean | null>(null);
  const [pending, setPending] = useState(false), [notice, setNotice] = useState<Notice>(null), [failure, setFailure] = useState<{ error: unknown; submitted: boolean } | null>(null);
  const active = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null), editor = useRef<HTMLFormElement>(null);
  const loadInitially = useEffectEvent(() => { void reload("initial"); });
  useEffect(() => {
    active.current = true; loadInitially();
    return () => { active.current = false; controller.current?.abort(); controller.current = null; };
  }, []);
  useEffect(() => { if (notice || failure || view.kind === "failed" || view.kind === "uncertain") feedback.current?.focus(); }, [notice, failure, view.kind]);
  const owns = (request: AbortController) => active.current && controller.current === request && !request.signal.aborted;
  function begin() {
    if (controller.current) return null;
    const request = new AbortController(); controller.current = request; setPending(true); return request;
  }
  function finish(request: AbortController) {
    if (controller.current !== request) return;
    controller.current = null; if (active.current) setPending(false);
  }
  async function checkSession(request: AbortController) {
    const current = await customerEmailApi.profile(request.signal);
    if (!owns(request)) return false;
    if (current.email !== customer.email) { onDifferent(current); return false; }
    return true;
  }
  async function snapshot(request: AbortController) {
    if (!await checkSession(request)) return null;
    const rows = await consentsApi.list(request.signal);
    if (!owns(request) || !await checkSession(request)) return null;
    return rows;
  }
  function accept(rows: Consent[], message: Notice) {
    setView({ kind: "ready", rows }); setChoice(initialChoice(rows.find(row => row.purpose === "Marketing"))); setFailure(null); setNotice(message);
  }
  async function reload(mode: "initial" | "refresh" | "review" = "refresh") {
    const request = begin(); if (!request) return;
    setView({ kind: "loading" }); setFailure(null); setNotice(null);
    try { const rows = await snapshot(request); if (rows && owns(request)) accept(rows, mode === "initial" ? null : mode === "review" ? "reviewed" : "refreshed"); }
    catch (error) {
      if (!owns(request)) return;
      if (statusOf(error) === 401) { onExpired(); return; }
      setView({ kind: mode === "review" ? "uncertain" : "failed", error });
    } finally { finish(request); }
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (view.kind !== "ready" || choice === null) return;
    const baseline = view.rows.find(row => row.purpose === "Marketing");
    if (!baseline || initialChoice(baseline) === choice) return;
    const request = begin(); if (!request) return;
    setFailure(null); setNotice(null); let submitted = false;
    try {
      const rows = await snapshot(request); if (!rows || !owns(request)) return;
      const current = rows.find(row => row.purpose === "Marketing");
      if (!current || !sameConsent(baseline, current)) { accept(rows, "changed"); return; }
      submitted = true; const saved = await consentsApi.setMarketing(choice, request.signal);
      if (!owns(request) || !await checkSession(request)) return;
      accept(rows.map(row => row.purpose === "Marketing" ? saved : row), "saved");
    } catch (error) {
      if (!owns(request)) return;
      if (statusOf(error) === 401) { onExpired(); return; }
      const refused = isHttpError(error) && error.kind === "http" && [400, 403, 404, 429].includes(error.status ?? 0);
      if (submitted && !refused) { setChoice(null); setView({ kind: "uncertain", error }); }
      else setFailure({ error, submitted });
    } finally { finish(request); }
  }
  const marketing = view.kind === "ready" ? view.rows.find(row => row.purpose === "Marketing") : undefined;
  const dirty = Boolean(marketing && choice !== initialChoice(marketing));
  return <AuthLayout className="consents-page" title={t("title")} introduction={<p>{t("intro")}</p>} footer={<><Link to="/account">{t("backAccount")}</Link><Link to="/account/privacy">{t("privacy")}</Link></>}>
    <div className="consents-workspace"><p className="consents-target">{t("target", { email: customer.email, store: store.name })}</p>
      {changed && <p role="status">{t("accountChanged")}</p>}
      {view.kind === "loading" && <LoadingState label={t("loading")} />}
      {(view.kind === "failed" || view.kind === "uncertain") && <div ref={feedback} tabIndex={-1} className="consents-feedback">
        {view.kind === "uncertain" && <p role="status">{t("uncertain")}</p>}
        <RequestError error={view.error} operation={view.kind === "uncertain" ? "write" : "read"} />
        <Button type="button" onClick={() => void reload(view.kind === "uncertain" ? "review" : "refresh")}>{t(view.kind === "uncertain" ? "review" : "reload")}</Button>
      </div>}
      {view.kind === "ready" && <>
        <p>{t("statementHint")}</p>
        {view.rows.length === 0 && <p>{t("empty")}</p>}
        {view.rows.map(row => <section key={row.purpose} className="consent-record" aria-label={row.purpose}>
          <h2>{row.purpose === "Marketing" ? t("marketing") : row.purpose}</h2><ConsentFacts row={row} />
          {row.purpose === "Marketing" ? <form ref={editor} aria-label={t("form")} onSubmit={event => void save(event)} aria-busy={pending || undefined}>
            <fieldset disabled={pending}><legend>{t("choice")}</legend>
              <label className="consent-option"><input type="radio" name="marketing" value="grant" checked={choice === true} onChange={() => { setChoice(true); setNotice(null); }} />{t("grant")}</label>
              <label className="consent-option"><input type="radio" name="marketing" value="decline" checked={choice === false} onChange={() => { setChoice(false); setNotice(null); }} />{t("decline")}</label>
            </fieldset>
            {dirty && <p className="hint" role="status">{t("unsaved")}</p>}
            <div className="consents-actions"><Button type="submit" disabled={!dirty || choice === null} busy={pending} busyLabel={t("saving")}>{t("save")}</Button>{dirty && <Button type="button" variant="secondary" disabled={pending} onClick={() => { setChoice(initialChoice(marketing)); setFailure(null); setNotice(null); requestAnimationFrame(() => { if (active.current) (editor.current?.querySelector<HTMLInputElement>("input:checked") ?? editor.current?.querySelector<HTMLInputElement>("input"))?.focus(); }); }}>{t("discard")}</Button>}</div>
          </form> : <p className="hint">{t("readOnly")}</p>}
        </section>)}
        {(notice || failure) && <div ref={feedback} tabIndex={-1} className="consents-feedback">
          {notice && <p role="status">{t(notice)}</p>}
          {failure && <><RequestError error={failure.error} operation={failure.submitted ? "write" : "read"} />{!failure.submitted && <p>{t("notSubmitted")}</p>}</>}
        </div>}
        <p>{t("orderMessages")}</p><Button type="button" variant="secondary" disabled={pending || dirty} onClick={() => void reload()}>{t("reload")}</Button>
      </>}
    </div>
  </AuthLayout>;
}
function ConsentFacts({ row }: { row: Consent }) {
  const { t, i18n } = useTranslation("consents");
  return <><p className="hint">{t("purpose")}: <bdi>{row.purpose}</bdi></p><blockquote className="consent-statement"><p>{row.statement}</p></blockquote>
    <dl className="consent-facts"><div><dt>{t("savedChoice")}</dt><dd>{t(row.decidedAt === null ? "unanswered" : row.isGranted ? "granted" : "declined")}</dd></div>
      <div><dt>{t("decidedAt")}</dt><dd>{row.decidedAt === null ? t("noDecision") : <time dateTime={row.decidedAt}>{new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: "medium", timeStyle: "short" }).format(new Date(row.decidedAt))}</time>}</dd></div>
    </dl></>;
}
