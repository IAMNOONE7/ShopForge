import { useEffect, useEffectEvent, useRef, useState, type FormEvent, type ReactNode, type Ref } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { isHttpError } from "../../api/http";
import { Button } from "../ui/Button";
import { Field } from "../ui/Field";
import "./PasswordRecoveryPages.css";

export type PasswordResetProof = { readonly realm: "staff" | "operator"; readonly id: number; available: () => boolean; take: () => string | null };
export type RecoveryContext = {
  realm: "staff" | "operator";
  brand: string;
  homePath: string;
  requestPath: string;
  api: {
    forgot: (email: string, signal: AbortSignal) => Promise<void>;
    reset: (token: string, password: string, signal: AbortSignal) => Promise<void>;
    session: (signal: AbortSignal) => Promise<{ email: string } | null>;
    signOut: (signal: AbortSignal) => Promise<void>;
  };
};

const status = (error: unknown) => isHttpError(error) && error.kind === "http" ? error.status : null;
function RecoveryLayout({ context, title, headingRef, children }: { context: RecoveryContext; title: string; headingRef?: Ref<HTMLHeadingElement>; children: ReactNode }) {
  const { t } = useTranslation(context.realm === "staff" ? "staffRecovery" : "operatorRecovery");
  useEffect(() => { document.title = `${title} · ${context.brand}`; }, [title, context.brand]);
  return <main className={`password-recovery-page ${context.realm}-recovery-page`}><p className="password-recovery-brand">{context.brand}</p><h1 ref={headingRef} tabIndex={-1}>{title}</h1>{children}<a href={context.homePath}>{t("backAdmin")}</a></main>;
}
export function ForgotPasswordPage({ context }: { context: RecoveryContext }) {
  return <ForgotPasswordForm key={context.realm} context={context} />;
}
function ForgotPasswordForm({ context }: { context: RecoveryContext }) {
  const { t } = useTranslation(context.realm === "staff" ? "staffRecovery" : "operatorRecovery");
  const [phase, setPhase] = useState<"editing" | "accepted" | "uncertain">("editing"), [email, setEmail] = useState(""), [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<"emailInvalid" | "limited" | null>(null);
  const active = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null), heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); controller.current = null; }; }, []);
  useEffect(() => { if (phase === "accepted") heading.current?.focus(); else if (phase === "uncertain" || failure) feedback.current?.focus(); }, [phase, failure]);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (controller.current || phase !== "editing") return;
    const address = email.trim();
    if (address.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(address)) { setFailure("emailInvalid"); return; }
    const request = new AbortController(); controller.current = request; setPending(true); setFailure(null); setEmail(address);
    try { await context.api.forgot(address, request.signal); if (active.current && !request.signal.aborted) setPhase("accepted"); }
    catch (error) {
      if (!active.current || request.signal.aborted) return;
      const code = status(error); if (code === 400 || code === 429) setFailure(code === 400 ? "emailInvalid" : "limited"); else setPhase("uncertain");
    } finally { if (controller.current === request) { controller.current = null; if (active.current) setPending(false); } }
  }
  return <RecoveryLayout context={context} title={t(phase === "accepted" ? "requestAcceptedTitle" : "forgotTitle")} headingRef={heading}>
    <p>{t("forgotIntro")}</p>
    {phase === "editing" && <form className={`password-recovery-form ${context.realm}-recovery-form`} aria-label={t("requestForm")} noValidate aria-busy={pending} onSubmit={event => void submit(event)}>
      <Field id={`${context.realm}-recovery-email`} name="email" type="email" autoComplete="username" maxLength={254} required label={t("email")} value={email} disabled={pending} error={failure === "emailInvalid" ? t("emailInvalid") : undefined} onChange={event => { setEmail(event.target.value); setFailure(null); }} />
      <Button type="submit" busy={pending} busyLabel={t("requesting")}>{t("request")}</Button>
    </form>}
    {(failure || phase !== "editing") && <div ref={feedback} tabIndex={-1} className={`password-recovery-feedback ${context.realm}-recovery-feedback`}>
      {failure && <div role="alert">{failure === "emailInvalid" ? <button type="button" className="link-button" onClick={() => document.getElementById(`${context.realm}-recovery-email`)?.focus()}>{t("emailInvalid")}</button> : <p>{t("limited")}</p>}</div>}
      {phase === "accepted" && <p role="status">{t("requestAccepted")}</p>}
      {phase === "uncertain" && <p role="alert">{t("requestUncertain")}</p>}
    </div>}
    <p className="hint">{t("latestLink")}</p>
  </RecoveryLayout>;
}

type Read = { kind: "checking" } | { kind: "guest" } | { kind: "member"; email: string } | { kind: "failed"; limited: boolean };
type Issue = "passwordInvalid" | "confirmationInvalid";
export function ResetPasswordPage({ context, proof }: { context: RecoveryContext; proof: PasswordResetProof | null }) {
  const scopedProof = proof?.realm === context.realm ? proof : null;
  return <ResetPasswordForm key={`${context.realm}:${scopedProof?.id ?? "missing"}`} context={context} proof={scopedProof} />;
}
function ResetPasswordForm({ context, proof }: { context: RecoveryContext; proof: PasswordResetProof | null }) {
  const { t } = useTranslation(context.realm === "staff" ? "staffRecovery" : "operatorRecovery");
  const [phase, setPhase] = useState<"open" | "closed" | "saved" | "uncertain">(() => proof?.available() ? "open" : "closed"), [read, setRead] = useState<Read>({ kind: "checking" });
  const [pending, setPending] = useState(false), [password, setPassword] = useState(""), [confirmation, setConfirmation] = useState(""), [shown, setShown] = useState(false);
  const [issues, setIssues] = useState<Issue[]>([]), [failure, setFailure] = useState<"limited" | "passwordRefused" | "signOutFailed" | null>(null);
  const active = useRef(false), controller = useRef<AbortController | null>(null), retryToken = useRef<string | null>(null), heading = useRef<HTMLHeadingElement>(null), feedback = useRef<HTMLDivElement>(null), firstField = useRef<HTMLInputElement>(null);
  const owns = (request: AbortController) => active.current && controller.current === request && !request.signal.aborted;
  const loadInitially = useEffectEvent((request: AbortController) => {
    void context.api.session(request.signal).then(member => { if (owns(request)) showSession(member); })
      .catch(error => { if (owns(request)) setRead({ kind: "failed", limited: status(error) === 429 }); })
      .finally(() => { if (controller.current === request) controller.current = null; });
  });
  useEffect(() => {
    active.current = true;
    if (proof?.available()) { const request = new AbortController(); controller.current = request; loadInitially(request); }
    return () => { active.current = false; controller.current?.abort(); controller.current = null; retryToken.current = null; };
  }, [proof]);
  const canReset = phase === "open" && read.kind === "guest" && Boolean(retryToken.current || proof?.available());
  useEffect(() => { if (canReset) firstField.current?.focus(); }, [canReset]);
  useEffect(() => { if (phase === "saved" || phase === "closed") heading.current?.focus(); else if (failure || issues.length || phase === "uncertain" || read.kind === "failed") feedback.current?.focus(); }, [phase, failure, issues, read.kind]);
  function showSession(member: { email: string } | null) { setRead(member ? { kind: "member", email: member.email } : { kind: "guest" }); }
  function begin() { if (controller.current || phase !== "open") return null; const request = new AbortController(); controller.current = request; setPending(true); setFailure(null); return request; }
  function finish(request: AbortController) { if (controller.current === request) { controller.current = null; if (active.current) setPending(false); } }
  function clearSecrets() { setPassword(""); setConfirmation(""); setShown(false); }
  async function check() {
    const request = begin(); if (!request) return;
    setRead({ kind: "checking" });
    try { const member = await context.api.session(request.signal); if (owns(request)) showSession(member); }
    catch (error) { if (owns(request)) setRead({ kind: "failed", limited: status(error) === 429 }); }
    finally { finish(request); }
  }
  async function signOut() {
    if (read.kind !== "member") return;
    const request = begin(); if (!request) return;
    clearSecrets();
    try { await context.api.signOut(request.signal); } catch { /* Only the following read establishes whether this browser is signed out. */ }
    if (!owns(request)) { finish(request); return; }
    try { const member = await context.api.session(request.signal); if (owns(request)) { showSession(member); if (member) setFailure("signOutFailed"); } }
    catch (error) { if (owns(request)) setRead({ kind: "failed", limited: status(error) === 429 }); }
    finally { finish(request); }
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); if (controller.current || !canReset) return;
    const invalid: Issue[] = [...(password.length < 10 || password.length > 128 ? ["passwordInvalid" as const] : []), ...(password !== confirmation ? ["confirmationInvalid" as const] : [])];
    setIssues(invalid); setFailure(null); if (invalid.length) return;
    const secret = password, request = begin(); if (!request) return;
    clearSecrets(); let credential: string | null = null;
    try {
      const member = await context.api.session(request.signal); if (!owns(request)) return;
      if (member) { showSession(member); return; }
      credential = retryToken.current ?? proof?.take() ?? null; retryToken.current = null;
      if (!credential) { setPhase("closed"); return; }
      await context.api.reset(credential, secret, request.signal);
      if (owns(request)) setPhase("saved");
    } catch (error) {
      if (!owns(request)) return;
      if (credential === null) { setRead({ kind: "failed", limited: status(error) === 429 }); return; }
      const code = status(error);
      if (code === 400 || code === 429) { retryToken.current = credential; setFailure(code === 400 ? "passwordRefused" : "limited"); }
      else setPhase(code === 404 ? "closed" : "uncertain");
    } finally { finish(request); }
  }
  return <RecoveryLayout context={context} title={t(phase === "saved" ? "resetSavedTitle" : phase === "closed" ? "closedTitle" : "resetTitle")} headingRef={heading}>
    {phase === "saved" ? <><p role="status">{t("resetSaved")}</p><p>{t("twoFactor")}</p><a className="button" href={context.homePath}>{t("signIn")}</a></> : phase === "closed" ? <p role="alert">{t("closed")}</p> : phase === "uncertain" ? <div ref={feedback} tabIndex={-1} className={`password-recovery-feedback ${context.realm}-recovery-feedback`}><p role="alert">{t("resetUncertain")}</p><a className="button" href={context.homePath}>{t("trySignIn")}</a></div> : <>
      <p>{t("resetIntro")}</p><p className="hint">{t("accountHint")}</p>
      {read.kind === "checking" && <p role="status">{t("checking")}</p>}
      {read.kind === "failed" && <div ref={feedback} tabIndex={-1} className={`password-recovery-feedback ${context.realm}-recovery-feedback`} role="alert"><p>{t(read.limited ? "limited" : "sessionFailed")}</p><Button type="button" disabled={pending} onClick={() => void check()}>{t("checkSession")}</Button></div>}
      {read.kind === "member" && <section className={`password-recovery-session ${context.realm}-recovery-session`}><h2>{t("alreadySignedIn")}</h2><p>{t("currentSession", { email: read.email })}</p><Button type="button" busy={pending} busyLabel={t("signingOut")} onClick={() => void signOut()}>{t("signOut")}</Button></section>}
      {(issues.length > 0 || failure !== null) && <div ref={feedback} tabIndex={-1} className={`password-recovery-feedback ${context.realm}-recovery-feedback`} role="alert">
        {issues.length > 0 ? <><p>{t("checkFields")}</p><ul>{issues.map(issue => <li key={issue}><button type="button" className="link-button" onClick={() => document.getElementById(issue === "passwordInvalid" ? `${context.realm}-reset-password` : `${context.realm}-reset-confirmation`)?.focus()}>{t(issue)}</button></li>)}</ul></> : failure && <p>{t(failure)}</p>}
      </div>}
      {(canReset || phase === "open" && read.kind === "guest" && pending) && <form className={`password-recovery-form ${context.realm}-recovery-form`} aria-label={t("resetForm")} aria-busy={pending} noValidate onSubmit={event => void submit(event)}>
        <Field ref={firstField} id={`${context.realm}-reset-password`} name="password" type={shown ? "text" : "password"} autoComplete="new-password" minLength={10} maxLength={128} required label={t("password")} hint={t("passwordHint")} value={password} disabled={pending} error={issues.includes("passwordInvalid") || failure === "passwordRefused" ? t("passwordInvalid") : undefined} onChange={event => { setPassword(event.target.value); setIssues([]); setFailure(null); }} />
        <Field id={`${context.realm}-reset-confirmation`} name="confirmation" type={shown ? "text" : "password"} autoComplete="new-password" minLength={10} maxLength={128} required label={t("confirmation")} value={confirmation} disabled={pending} error={issues.includes("confirmationInvalid") ? t("confirmationInvalid") : undefined} onChange={event => { setConfirmation(event.target.value); setIssues([]); }} />
        <Button type="button" variant="quiet" aria-controls={`${context.realm}-reset-password ${context.realm}-reset-confirmation`} aria-pressed={shown} disabled={pending} onClick={() => setShown(value => !value)}>{t(shown ? "hidePasswords" : "showPasswords")}</Button>
        <Button type="submit" busy={pending} busyLabel={t("resetting")}>{t("reset")}</Button>
      </form>}
    </>}
    {phase !== "saved" && <Link to={context.requestPath}>{t("newLink")}</Link>}
    {phase !== "saved" && <p className="hint">{t("latestLink")}</p>}
  </RecoveryLayout>;
}
