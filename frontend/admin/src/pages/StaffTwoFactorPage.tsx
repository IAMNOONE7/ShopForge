import { useEffect, useEffectEvent, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { CurrentUser } from "../api";
import { isHttpError } from "../api/http";
import { sameStaffAccount } from "../api/staffAccount";
import { staffTwoFactorApi, type StaffSecuritySession, type StaffTwoFactorSetup } from "../api/staffTwoFactor";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { useSession } from "../session";
import "../components/auth/AccountSecurityPage.css";
import "./StaffTwoFactorPage.css";

type Action = "begin" | "confirm" | "off";
type Step =
  | { kind: "status"; enabled: boolean }
  | { kind: "setup"; material: StaffTwoFactorSetup }
  | { kind: "off" }
  | { kind: "recovery"; codes: string[] }
  | { kind: "uncertain"; action: Action; observed: boolean | null };
type View = Step | { kind: "loading" } | { kind: "read-failed"; resume: Step | null } | { kind: "closed"; reason: "expired" | "changed" | "settingsChanged" };
type Command = { action: "begin" } | { action: "confirm"; code: string } | { action: "off"; password: string };
type Failure = "codeInvalid" | "addedRequired" | "passwordRequired" | "offRequired" | "wrongCode" | "wrongPassword" | "limited" | "notSubmitted" | "refused" | "conflict";
const status = (error: unknown) => isHttpError(error) && error.kind === "http" ? error.status : null;
function expectedEnabled(step: Step): boolean | null {
  if (step.kind === "status") return step.enabled;
  if (step.kind === "setup") return false;
  if (step.kind === "off" || step.kind === "recovery") return true;
  return null;
}

export function StaffTwoFactorPage() {
  const { user } = useSession();
  return <StaffTwoFactorForm key={JSON.stringify([user.id, user.tenantId, user.role, user.email.trim().toLowerCase()])} user={user} />;
}

function StaffTwoFactorForm({ user }: { user: CurrentUser }) {
  const { t } = useTranslation("staffTwoFactor");
  const [view, setView] = useState<View>({ kind: "loading" });
  const [pending, setPending] = useState(false);
  const [shown, setShown] = useState(false);
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const [added, setAdded] = useState(false);
  const [offConfirmed, setOffConfirmed] = useState(false);
  const [saved, setSaved] = useState(false);
  const [failure, setFailure] = useState<Failure | null>(null);
  const [notice, setNotice] = useState<"discarded" | "disabled" | "finished" | null>(null);
  const controller = useRef<AbortController | null>(null);
  const active = useRef(false);
  const feedback = useRef<HTMLDivElement>(null);
  const panel = useRef<HTMLElement>(null);
  const offTrigger = useRef<HTMLButtonElement>(null);
  const keyInput = useRef<HTMLInputElement>(null);
  const codesInput = useRef<HTMLTextAreaElement>(null);
  const owns = (request: AbortController) => active.current && controller.current === request && !request.signal.aborted;
  function clearInput() { setCode(""); setPassword(""); setShown(false); }
  function close(reason: "expired" | "changed" | "settingsChanged") {
    clearInput(); setAdded(false); setOffConfirmed(false); setSaved(false); setFailure(null); setNotice(null); setView({ kind: "closed", reason });
  }
  function denied(error: unknown) {
    const code = status(error); if (code !== 401 && code !== 403) return false;
    close(code === 401 ? "expired" : "changed"); return true;
  }
  function same(session: StaffSecuritySession) { if (!sameStaffAccount(user, session)) { close("changed"); return false; } return true; }
  const initialRead = useEffectEvent((request: AbortController) => {
    void staffTwoFactorApi.session(request.signal)
      .then(session => { if (owns(request) && same(session)) setView({ kind: "status", enabled: session.isTwoFactorEnabled }); })
      .catch(error => { if (owns(request) && !denied(error)) { setView({ kind: "read-failed", resume: null }); setFailure(status(error) === 429 ? "limited" : null); } })
      .finally(() => { if (controller.current === request) controller.current = null; });
  });
  useEffect(() => {
    active.current = true;
    const request = new AbortController(); controller.current = request; initialRead(request);
    return () => { active.current = false; controller.current?.abort(); controller.current = null; };
  }, []);
  useEffect(() => { if (failure || notice || view.kind === "closed" || view.kind === "read-failed" || view.kind === "uncertain") feedback.current?.focus(); }, [failure, notice, view.kind]);
  useEffect(() => { if (["setup", "recovery", "off"].includes(view.kind)) panel.current?.focus(); }, [view.kind]);
  function beginRequest() {
    if (controller.current) return null;
    const request = new AbortController(); controller.current = request; setPending(true); setFailure(null); setNotice(null); return request;
  }
  function finishRequest(request: AbortController) {
    if (controller.current === request) { controller.current = null; if (active.current) setPending(false); }
  }
  function apply(session: StaffSecuritySession, resume: Step | null) {
    if (!same(session)) return;
    if (resume?.kind === "uncertain") { setView({ ...resume, observed: session.isTwoFactorEnabled }); return; }
    if (resume && expectedEnabled(resume) !== null && expectedEnabled(resume) !== session.isTwoFactorEnabled) { close("settingsChanged"); return; }
    setView(resume ?? { kind: "status", enabled: session.isTwoFactorEnabled });
  }
  async function readAgain() {
    if (view.kind !== "read-failed" && view.kind !== "uncertain" && view.kind !== "status") return;
    const resume = view.kind === "read-failed" ? view.resume : view.kind === "uncertain" ? view : null;
    const request = beginRequest(); if (!request) return;
    clearInput();
    try { const session = await staffTwoFactorApi.session(request.signal); if (owns(request)) apply(session, resume); }
    catch (error) { if (owns(request) && !denied(error)) { setView({ kind: "read-failed", resume }); setFailure(status(error) === 429 ? "limited" : null); } }
    finally { finishRequest(request); }
  }
  async function change(command: Command) {
    if (view.kind !== "status" && view.kind !== "setup" && view.kind !== "off") return;
    if (command.action === "begin" && (view.kind !== "status" || view.enabled) || command.action === "confirm" && view.kind !== "setup" || command.action === "off" && view.kind !== "off") return;
    const request = beginRequest(); if (!request) return;
    clearInput(); setSaved(false);
    let submitted = false;
    try {
      const before = await staffTwoFactorApi.session(request.signal);
      if (!owns(request) || !same(before)) return;
      const expected = command.action === "off";
      if (before.isTwoFactorEnabled !== expected) { close("settingsChanged"); return; }
      submitted = true;
      let next: Step;
      if (command.action === "begin") next = { kind: "setup", material: await staffTwoFactorApi.begin(before.email, request.signal) };
      else if (command.action === "confirm") next = { kind: "recovery", codes: await staffTwoFactorApi.confirm(command.code, request.signal) };
      else { await staffTwoFactorApi.turnOff(command.password, request.signal); next = { kind: "status", enabled: false }; }
      if (!owns(request)) return;
      setView({ kind: "loading" }); setAdded(false); setOffConfirmed(false);
      try {
        const after = await staffTwoFactorApi.session(request.signal);
        if (!owns(request) || !same(after)) return;
        const expectedAfter = command.action === "confirm";
        if (after.isTwoFactorEnabled !== expectedAfter) { close("settingsChanged"); return; }
        setView(next); if (command.action === "off") setNotice("disabled");
      } catch (error) {
        if (owns(request) && !denied(error)) { setView({ kind: "read-failed", resume: next }); setFailure(status(error) === 429 ? "limited" : null); }
      }
    } catch (error) {
      if (!owns(request)) return;
      const code = status(error);
      if (submitted && code === 403 && command.action !== "begin") { setFailure(command.action === "confirm" ? "wrongCode" : "wrongPassword"); return; }
      if (denied(error)) return;
      if (!submitted) { setView({ kind: "read-failed", resume: view }); setFailure(code === 429 ? "limited" : "notSubmitted"); }
      else if (code === 400 || code === 429) setFailure(code === 429 ? "limited" : "refused");
      else if (code === 409) { setView({ kind: "read-failed", resume: null }); setAdded(false); setOffConfirmed(false); setFailure("conflict"); }
      else { setView({ kind: "uncertain", action: command.action, observed: null }); setAdded(false); setOffConfirmed(false); }
    } finally { finishRequest(request); }
  }
  function submit(event: FormEvent) {
    event.preventDefault(); if (pending || controller.current) return;
    if (view.kind === "setup") {
      if (!added) { setFailure("addedRequired"); return; }
      if (!/^[0-9]{6}$/.test(code)) { setFailure("codeInvalid"); return; }
      void change({ action: "confirm", code });
    } else if (view.kind === "off") {
      if (!offConfirmed) { setFailure("offRequired"); return; }
      if (!password) { setFailure("passwordRequired"); return; }
      void change({ action: "off", password });
    }
  }
  function returnToStatus(enabled: boolean) {
    if (controller.current) return;
    clearInput(); setAdded(false); setOffConfirmed(false); setSaved(false); setFailure(null); setView({ kind: "status", enabled });
  }
  function cancelOff() { returnToStatus(true); requestAnimationFrame(() => { if (active.current) offTrigger.current?.focus(); }); }
  const resumedMaterial = view.kind === "read-failed" && (view.resume?.kind === "setup" || view.resume?.kind === "recovery");
  return <div className="account-security-page staff-account-page staff-two-factor-page" onKeyDown={event => { if (event.key === "Escape" && view.kind === "off" && !pending) { event.preventDefault(); cancelOff(); } }}>
    <h1>{t("title")}</h1><p>{t("intro", { email: user.email })}</p>
    <Link to="/account">{t("back")}</Link>
    {view.kind === "loading" && <p role="status">{t("checking")}</p>}
    <div ref={feedback} tabIndex={-1} className="account-security-feedback staff-account-feedback">
      {failure && <div role="alert"><p>{t(failure)}</p>{["codeInvalid", "addedRequired", "passwordRequired", "offRequired", "wrongCode", "wrongPassword"].includes(failure) && <button type="button" className="link-button" onClick={() => document.getElementById(failure === "addedRequired" ? "two-factor-added" : failure === "offRequired" ? "two-factor-off-confirmed" : failure === "passwordRequired" || failure === "wrongPassword" ? "two-factor-password" : "two-factor-code")?.focus()}>{t("goToField")}</button>}</div>}
      {notice && <p role="status">{t(notice)}</p>}
      {view.kind === "closed" && <><p role="alert">{t(view.reason)}</p><a href={`${import.meta.env.BASE_URL}account/two-factor`}>{t("reload")}</a></>}
      {view.kind === "read-failed" && <><p role="alert">{t(resumedMaterial ? "materialCheckFailed" : "readFailed")}</p><Button type="button" busy={pending} busyLabel={t("checking")} onClick={() => void readAgain()}>{t("checkStatus")}</Button></>}
      {view.kind === "uncertain" && <><p role="alert">{t(view.action === "confirm" ? "confirmUncertain" : view.action === "begin" ? "beginUncertain" : "offUncertain")}</p>{view.observed !== null && <p>{t(view.observed ? "observedOn" : "observedOff")}</p>}<Button type="button" variant="secondary" busy={pending} busyLabel={t("checking")} onClick={() => void readAgain()}>{t("checkStatus")}</Button><a href={`${import.meta.env.BASE_URL}account/two-factor`}>{t("reload")}</a></>}
    </div>
    {view.kind === "status" && <section className="account-security-panel staff-account-panel" aria-labelledby="two-factor-status-title"><h2 id="two-factor-status-title">{t(view.enabled ? "on" : "off")}</h2><p>{t(view.enabled ? "onHelp" : "offHelp")}</p><p className="hint">{t("sessionsUnchanged")}</p><div className="account-security-actions staff-account-actions">
      {view.enabled ? <Button ref={offTrigger} type="button" variant="secondary" disabled={pending} onClick={() => { clearInput(); setFailure(null); setNotice(null); setView({ kind: "off" }); }}>{t("turnOff")}</Button> : <Button type="button" busy={pending} busyLabel={t("starting")} onClick={() => void change({ action: "begin" })}>{t("start")}</Button>}
      <Button type="button" variant="quiet" disabled={pending} onClick={() => void readAgain()}>{t("refresh")}</Button>
    </div></section>}
    {view.kind === "setup" && <section ref={panel} tabIndex={-1} className="account-security-panel staff-account-panel" aria-labelledby="two-factor-setup-title"><h2 id="two-factor-setup-title">{t("setupTitle")}</h2><p>{t("setupHelp", { email: user.email })}</p><p className="hint">{t("manualSettings")}</p><p>{t("setupPrivacy")}</p>
      <div className="account-security-actions staff-account-actions"><Button type="button" variant="secondary" aria-expanded={shown} aria-controls="two-factor-key" disabled={pending} onClick={() => setShown(value => !value)}>{t(shown ? "hideKey" : "showKey")}</Button>{shown && <Button type="button" variant="quiet" disabled={pending} onClick={() => { keyInput.current?.focus(); keyInput.current?.select(); }}>{t("selectKey")}</Button>}</div>
      {shown && <Field ref={keyInput} id="two-factor-key" label={t("key")} hint={t("selectHint")} readOnly autoComplete="off" spellCheck={false} value={view.material.secret} />}
      <form className="account-security-form staff-account-form" aria-label={t("confirmForm")} noValidate aria-busy={pending} onSubmit={submit}>
        <label className="staff-two-factor-check"><input id="two-factor-added" type="checkbox" checked={added} disabled={pending} onChange={event => { setAdded(event.target.checked); setFailure(null); }} />{t("added")}</label>
        <Field id="two-factor-code" name="code" label={t("code")} hint={t("codeHint")} autoComplete="one-time-code" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} required value={code} disabled={pending} error={failure === "codeInvalid" || failure === "wrongCode" ? t(failure) : undefined} onChange={event => { setCode(event.target.value); setFailure(null); }} />
        <div className="account-security-actions staff-account-actions"><Button type="submit" busy={pending} busyLabel={t("confirming")}>{t("confirm")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={() => { returnToStatus(false); setNotice("discarded"); }}>{t("discard")}</Button></div>
      </form>
    </section>}
    {view.kind === "recovery" && <section ref={panel} tabIndex={-1} className="account-security-panel staff-account-panel" aria-labelledby="two-factor-recovery-title"><h2 id="two-factor-recovery-title">{t("recoveryTitle")}</h2><p role="status">{t("enabled")}</p><p>{t("recoveryHelp")}</p><p>{t("recoveryPrivacy")}</p>
      <div className="account-security-actions staff-account-actions"><Button type="button" variant="secondary" aria-expanded={shown} aria-controls="two-factor-codes" onClick={() => setShown(value => !value)}>{t(shown ? "hideCodes" : "showCodes")}</Button>{shown && <Button type="button" variant="quiet" onClick={() => { codesInput.current?.focus(); codesInput.current?.select(); }}>{t("selectCodes")}</Button>}</div>
      {shown && <div className="staff-two-factor-codes"><label htmlFor="two-factor-codes">{t("codes")}</label><textarea ref={codesInput} id="two-factor-codes" aria-describedby="two-factor-codes-hint" rows={10} wrap="off" readOnly autoComplete="off" spellCheck={false} value={view.codes.join("\n")} /><span id="two-factor-codes-hint" className="hint">{t("selectHint")}</span></div>}
      <label className="staff-two-factor-check"><input type="checkbox" checked={saved} disabled={!shown && !saved} onChange={event => setSaved(event.target.checked)} />{t("saved")}</label>
      <Button type="button" disabled={!saved} onClick={() => { returnToStatus(true); setNotice("finished"); }}>{t("finish")}</Button>
    </section>}
    {view.kind === "off" && <section ref={panel} tabIndex={-1} className="account-security-panel staff-account-panel" aria-labelledby="two-factor-off-title"><h2 id="two-factor-off-title">{t("offTitle")}</h2><p>{t("offWarning")}</p>
      <form className="account-security-form staff-account-form" aria-label={t("offForm")} noValidate aria-busy={pending} onSubmit={submit}>
        <label className="staff-two-factor-check"><input id="two-factor-off-confirmed" type="checkbox" checked={offConfirmed} disabled={pending} onChange={event => { setOffConfirmed(event.target.checked); setFailure(null); }} />{t("offAcknowledgment")}</label>
        <Field id="two-factor-password" name="password" type={shown ? "text" : "password"} autoComplete="current-password" required label={t("password")} value={password} disabled={pending} error={failure === "passwordRequired" || failure === "wrongPassword" ? t(failure) : undefined} onChange={event => { setPassword(event.target.value); setFailure(null); }} />
        <div className="account-security-actions staff-account-actions"><Button type="button" variant="quiet" aria-pressed={shown} aria-controls="two-factor-password" disabled={pending} onClick={() => setShown(value => !value)}>{t(shown ? "hidePassword" : "showPassword")}</Button><Button type="submit" variant="danger" busy={pending} busyLabel={t("disabling")}>{t("confirmOff")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={cancelOff}>{t("cancel")}</Button></div>
      </form>
    </section>}
  </div>;
}
