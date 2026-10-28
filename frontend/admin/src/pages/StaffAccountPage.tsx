import { useEffect, useEffectEvent, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { CurrentUser } from "../api";
import { isHttpError } from "../api/http";
import { sameStaffAccount, staffAccountApi } from "../api/staffAccount";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { useSession } from "../session";
import "./StaffAccountPage.css";

type Action = "password" | "sessions";
type View = "loading" | "ready" | "read-failed" | "expired" | "changed";
type Result = { action: Action; acknowledged: boolean; checked: boolean };
type Issue = "currentRequired" | "passwordInvalid" | "confirmationInvalid";
type Failure = "wrongCurrent" | "passwordRefused" | "limited" | "notSubmitted" | "refused";
const status = (error: unknown) => isHttpError(error) && error.kind === "http" ? error.status : null;

export function StaffAccountPage() {
  const { user } = useSession();
  return <StaffAccountForm key={JSON.stringify([user.id, user.tenantId, user.role, user.email.trim().toLowerCase()])} user={user} />;
}

function StaffAccountForm({ user }: { user: CurrentUser }) {
  const { t } = useTranslation("staffAccount");
  const [view, setView] = useState<View>("loading");
  const [pending, setPending] = useState(false);
  const [current, setCurrent] = useState("");
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [shown, setShown] = useState(false);
  const [confirmSessions, setConfirmSessions] = useState(false);
  const [issues, setIssues] = useState<Issue[]>([]);
  const [failure, setFailure] = useState<Failure | null>(null);
  const [result, setResult] = useState<Result | null>(null);
  const active = useRef(false);
  const controller = useRef<AbortController | null>(null);
  const feedback = useRef<HTMLDivElement>(null);
  const sessionPanel = useRef<HTMLDivElement>(null);
  const sessionTrigger = useRef<HTMLButtonElement>(null);
  const owns = (request: AbortController) => active.current && controller.current === request && !request.signal.aborted;

  function clearSecrets() { setCurrent(""); setPassword(""); setConfirmation(""); setShown(false); }
  async function checkSession(request: AbortController) {
    const actual = await staffAccountApi.session(request.signal);
    if (!owns(request)) return false;
    if (!sameStaffAccount(user, actual)) { clearSecrets(); setConfirmSessions(false); setView("changed"); return false; }
    return true;
  }
  function denied(error: unknown) {
    if (status(error) !== 401 && status(error) !== 403) return false;
    clearSecrets(); setConfirmSessions(false); setView(status(error) === 401 ? "expired" : "changed"); return true;
  }
  const initialRead = useEffectEvent((request: AbortController) => {
    void staffAccountApi.session(request.signal)
      .then(actual => { if (owns(request)) setView(sameStaffAccount(user, actual) ? "ready" : "changed"); })
      .catch(error => { if (owns(request) && !denied(error)) { setFailure(status(error) === 429 ? "limited" : null); setView("read-failed"); } })
      .finally(() => { if (controller.current === request) controller.current = null; });
  });
  useEffect(() => {
    active.current = true;
    const request = new AbortController(); controller.current = request; initialRead(request);
    return () => { active.current = false; controller.current?.abort(); controller.current = null; };
  }, []);
  useEffect(() => { if (failure || issues.length || result || ["read-failed", "expired", "changed"].includes(view)) feedback.current?.focus(); }, [failure, issues, result, view]);
  useEffect(() => { if (confirmSessions) sessionPanel.current?.focus(); }, [confirmSessions]);

  const blocked = view !== "ready" || Boolean(result && (!result.acknowledged || !result.checked));
  function begin() {
    if (controller.current) return null;
    const request = new AbortController(); controller.current = request; setPending(true); setFailure(null); setIssues([]); return request;
  }
  function finish(request: AbortController) {
    if (controller.current === request) { controller.current = null; if (active.current) setPending(false); }
  }
  async function readAgain() {
    if (view === "expired" || view === "changed") return;
    const request = begin(); if (!request) return;
    clearSecrets();
    try {
      if (await checkSession(request) && owns(request)) { setView("ready"); if (result) setResult({ ...result, checked: true }); }
    } catch (error) { if (owns(request) && !denied(error)) { setView("read-failed"); setFailure(status(error) === 429 ? "limited" : null); } }
    finally { finish(request); }
  }
  async function change(action: Action, credentials?: { current: string; password: string }) {
    if (blocked || action === "sessions" && !confirmSessions) return;
    const request = begin(); if (!request) return;
    clearSecrets(); setResult(null);
    let submitted = false;
    try {
      if (!await checkSession(request) || !owns(request)) return;
      submitted = true;
      if (action === "password" && credentials) await staffAccountApi.password(credentials.current, credentials.password, request.signal);
      else await staffAccountApi.endOtherSessions(request.signal);
      if (!owns(request)) return;
      setConfirmSessions(false);
      setResult({ action, acknowledged: true, checked: false });
      try {
        if (await checkSession(request) && owns(request)) setResult({ action, acknowledged: true, checked: true });
      } catch (error) { if (owns(request) && !denied(error)) { setView("read-failed"); setFailure(status(error) === 429 ? "limited" : null); } }
    } catch (error) {
      if (!owns(request)) return;
      const code = status(error);
      if (submitted && code === 403 && action === "password") { setFailure("wrongCurrent"); return; }
      if (denied(error)) return;
      if (!submitted) { setView("read-failed"); setFailure(code === 429 ? "limited" : "notSubmitted"); }
      else if (code === 400 || code === 429) setFailure(code === 429 ? "limited" : action === "password" ? "passwordRefused" : "refused");
      else { setConfirmSessions(false); setResult({ action, acknowledged: false, checked: false }); }
    } finally { finish(request); }
  }
  function submit(event: FormEvent) {
    event.preventDefault(); if (pending || blocked || confirmSessions) return;
    const invalid: Issue[] = [
      ...(!current ? ["currentRequired" as const] : []),
      ...(password.length < 10 || password.length > 128 ? ["passwordInvalid" as const] : []),
      ...(confirmation !== password ? ["confirmationInvalid" as const] : []),
    ];
    setIssues(invalid); setFailure(null); setResult(null);
    if (!invalid.length) void change("password", { current, password });
  }
  const inputDisabled = pending || blocked || confirmSessions;
  function cancelSessions() {
    if (pending) return;
    setConfirmSessions(false); setFailure(null);
    requestAnimationFrame(() => { if (active.current) sessionTrigger.current?.focus(); });
  }
  return <div className="staff-account-page">
    <h1>{t("title")}</h1>
    <p>{t("intro", { email: user.email })}</p>
    <Link to="/account/two-factor">{t("twoFactor")}</Link>
    {view === "loading" && <p role="status">{t("checking")}</p>}
    <div ref={feedback} tabIndex={-1} className="staff-account-feedback">
      {issues.length > 0 && <div role="alert"><p>{t("checkFields")}</p><ul>{issues.map(issue => <li key={issue}><button type="button" className="link-button" onClick={() => document.getElementById(issue === "currentRequired" ? "account-current" : issue === "passwordInvalid" ? "account-password" : "account-confirmation")?.focus()}>{t(issue)}</button></li>)}</ul></div>}
      {failure && <p role="alert">{t(failure)}</p>}
      {(view === "expired" || view === "changed") ? <><p role="alert">{t(view === "expired" ? "expired" : "changed")}</p><a href={`${import.meta.env.BASE_URL}account`}>{t("reload")}</a></> : <>
        {view === "read-failed" && <p role="alert">{t("readFailed")}</p>}
        {result && <p role={result.acknowledged && (result.checked || pending) ? "status" : "alert"}>{t(result.acknowledged ? result.checked ? result.action === "password" ? "passwordSaved" : "sessionsEnded" : pending ? "checking" : "confirmedCheckFailed" : result.action === "password" ? "passwordUncertain" : "sessionsUncertain")}</p>}
        {result?.checked && !result.acknowledged && <p>{t("sessionChecked")}</p>}
        {(view === "read-failed" || result && (!result.checked || !result.acknowledged)) && <Button type="button" variant="secondary" busy={pending} busyLabel={t("checking")} onClick={() => void readAgain()}>{t("checkSession")}</Button>}
        {result && !result.acknowledged && <a href={`${import.meta.env.BASE_URL}account`}>{t("reload")}</a>}
        {result?.action === "password" && !result.acknowledged && <a href={`${import.meta.env.BASE_URL}forgot-password`}>{t("recoverPassword")}</a>}
      </>}
    </div>
    {view !== "loading" && view !== "expired" && view !== "changed" && <>
      <section className="staff-account-panel" aria-labelledby="account-password-title"><h2 id="account-password-title">{t("passwordTitle")}</h2><p>{t("passwordHelp")}</p>
        <form aria-label={t("passwordForm")} className="staff-account-form" noValidate aria-busy={pending} onSubmit={submit}>
          <Field id="account-current" name="currentPassword" type={shown ? "text" : "password"} autoComplete="current-password" required label={t("current")} value={current} disabled={inputDisabled} error={issues.includes("currentRequired") || failure === "wrongCurrent" ? t(failure === "wrongCurrent" ? "wrongCurrent" : "currentRequired") : undefined} onChange={event => { setCurrent(event.target.value); setIssues([]); setFailure(null); }} />
          <Field id="account-password" name="newPassword" type={shown ? "text" : "password"} autoComplete="new-password" minLength={10} maxLength={128} required label={t("password")} hint={t("passwordHint")} value={password} disabled={inputDisabled} error={issues.includes("passwordInvalid") || failure === "passwordRefused" ? t("passwordInvalid") : undefined} onChange={event => { setPassword(event.target.value); setIssues([]); setFailure(null); }} />
          <Field id="account-confirmation" name="confirmation" type={shown ? "text" : "password"} autoComplete="new-password" minLength={10} maxLength={128} required label={t("confirmation")} value={confirmation} disabled={inputDisabled} error={issues.includes("confirmationInvalid") ? t("confirmationInvalid") : undefined} onChange={event => { setConfirmation(event.target.value); setIssues([]); }} />
          <div className="staff-account-actions"><Button type="button" variant="quiet" aria-controls="account-current account-password account-confirmation" aria-pressed={shown} disabled={inputDisabled} onClick={() => setShown(value => !value)}>{t(shown ? "hidePasswords" : "showPasswords")}</Button><Button type="submit" busy={pending} busyLabel={t("saving")} disabled={blocked || confirmSessions}>{t("savePassword")}</Button></div>
        </form>
      </section>
      <section className="staff-account-panel" aria-labelledby="account-sessions-title"><h2 id="account-sessions-title">{t("sessionsTitle")}</h2><p>{t("sessionsHelp")}</p>
        {confirmSessions ? <div ref={sessionPanel} tabIndex={-1} className="staff-account-confirm" role="region" aria-label={t("confirmTitle")} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); cancelSessions(); } }}><h3>{t("confirmTitle")}</h3><p>{t("confirmHelp", { email: user.email })}</p><div className="staff-account-actions"><Button type="button" variant="danger" busy={pending} busyLabel={t("ending")} disabled={blocked} onClick={() => void change("sessions")}>{t("confirmSessions")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={cancelSessions}>{t("cancel")}</Button></div></div>
          : <Button ref={sessionTrigger} type="button" variant="secondary" disabled={pending || blocked} onClick={() => { clearSecrets(); setIssues([]); setFailure(null); setResult(null); setConfirmSessions(true); }}>{t("endSessions")}</Button>}
      </section>
    </>}
  </div>;
}
