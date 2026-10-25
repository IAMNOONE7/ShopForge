import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { invitationsApi, type OpenInvitation } from "../api/invitations";
import { isHttpError } from "../api/http";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import type { InvitationEntry } from "../invitationEntry";
import { adminHomePath } from "../routing";
import "./InvitationAcceptancePage.css";

type Read = { kind: "loading" } | { kind: "closed" } | { kind: "failed"; limited: boolean } | { kind: "ready"; invitation: OpenInvitation; member: { email: string } | null };
type Outcome = "passwordInvalid" | "limited" | "conflict" | "closed" | "uncertain" | "accepted" | "signOutFailed" | null;
type Issue = "passwordInvalid" | "confirmationInvalid";
function status(error: unknown) { return isHttpError(error) && error.kind === "http" ? error.status : null; }

export function InvitationAcceptancePage({ entry }: { entry: InvitationEntry }) {
  const { t, i18n } = useTranslation(["invitations", "auth"]);
  const [read, setRead] = useState<Read>({ kind: entry.token ? "loading" : "closed" });
  const [outcome, setOutcome] = useState<Outcome>(null), [uncertain, setUncertain] = useState(false), [pending, setPending] = useState(false);
  const [password, setPassword] = useState(""), [confirmation, setConfirmation] = useState(""), [shown, setShown] = useState(false);
  const [issues, setIssues] = useState<Issue[]>([]), [acceptedEmail, setAcceptedEmail] = useState("");
  const active = useRef(false), lock = useRef(false), request = useRef<AbortController | null>(null), heading = useRef<HTMLHeadingElement>(null), feedback = useRef<HTMLDivElement>(null), firstField = useRef<HTMLInputElement>(null);
  const token = useRef(entry.token);
  useEffect(() => { document.title = t(outcome === "accepted" ? "invitations:acceptedTitle" : "invitations:title") + " · ShopForge Admin"; }, [t, i18n.resolvedLanguage, outcome]);
  useEffect(() => {
    active.current = true;
    if (token.current) void refresh();
    return () => { active.current = false; request.current?.abort(); };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const issueKey = issues.join(",");
  useEffect(() => { if (outcome === "accepted" || outcome === "closed") heading.current?.focus(); else if (outcome || issueKey) feedback.current?.focus(); }, [outcome, issueKey]);
  const canAccept = read.kind === "ready" && read.member === null && !uncertain && !["accepted", "closed", "conflict"].includes(outcome ?? "");
  useEffect(() => { if (canAccept) firstField.current?.focus(); }, [canAccept]);
  const newRequest = () => { request.current?.abort(); const next = new AbortController(); request.current = next; return next; };
  const owns = (controller: AbortController) => active.current && !controller.signal.aborted && request.current === controller;
  async function refresh() {
    if (!token.current || lock.current) return;
    const controller = newRequest(); setRead({ kind: "loading" });
    try {
      const [invitation, member] = await Promise.all([invitationsApi.details(token.current, controller.signal), invitationsApi.session(controller.signal)]);
      if (owns(controller)) setRead({ kind: "ready", invitation, member });
    } catch (error) {
      if (owns(controller)) setRead(status(error) === 404 ? { kind: "closed" } : { kind: "failed", limited: status(error) === 429 });
    }
  }
  async function signOut() {
    if (lock.current || read.kind !== "ready" || read.member === null) return;
    lock.current = true; setPending(true); setOutcome(null); const controller = newRequest();
    try { await invitationsApi.signOut(controller.signal); } catch { /* The session read determines whether signing out reached the browser. */ }
    try {
      const member = await invitationsApi.session(controller.signal);
      if (owns(controller)) { setRead({ ...read, member }); if (member) setOutcome("signOutFailed"); }
    } catch { if (owns(controller)) setRead({ kind: "failed", limited: false }); }
    finally { lock.current = false; if (owns(controller)) setPending(false); }
  }
  async function accept(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current || !canAccept || read.kind !== "ready" || !token.current) return;
    const next: Issue[] = [];
    if (password.length < 10 || password.length > 128) next.push("passwordInvalid");
    if (password !== confirmation) next.push("confirmationInvalid");
    setIssues(next); if (next.length) return;
    const secret = password, invitation = read.invitation, credential = token.current;
    lock.current = true; setPending(true); setOutcome(null); setPassword(""); setConfirmation(""); setShown(false);
    const controller = newRequest(); let submitted = false;
    try {
      const member = await invitationsApi.session(controller.signal);
      if (!owns(controller)) return;
      if (member) { setRead({ kind: "ready", invitation, member }); return; }
      submitted = true;
      const accepted = await invitationsApi.accept(credential, secret, invitation, controller.signal);
      if (owns(controller)) { setAcceptedEmail(accepted.email); setOutcome("accepted"); token.current = null; heading.current?.focus(); }
    } catch (error) {
      if (!owns(controller)) return;
      if (!submitted) { setRead({ kind: "failed", limited: status(error) === 429 }); return; }
      const code = status(error);
      if (code === 400) { setOutcome("passwordInvalid"); setIssues(["passwordInvalid"]); }
      else if (code === 404) { setOutcome("closed"); setRead({ kind: "closed" }); token.current = null; }
      else if (code === 409) setOutcome("conflict");
      else if (code === 429) setOutcome("limited");
      else { setUncertain(true); setOutcome("uncertain"); }
    } finally { lock.current = false; if (owns(controller)) setPending(false); }
  }
  const invitation = read.kind === "ready" ? read.invitation : null;
  return <main className="invitation-page">
    <p className="invitation-brand">ShopForge Admin</p><h1 ref={heading} tabIndex={-1}>{t(outcome === "accepted" ? "invitations:acceptedTitle" : "invitations:title")}</h1>
    {outcome !== "accepted" && <p>{t("invitations:intro")}</p>}
    {outcome === "accepted" ? <><p role="status">{t("invitations:acceptedBody", { email: acceptedEmail })}</p><a className="button" href={adminHomePath()}>{t("auth:signIn")}</a></> : <>
      {read.kind === "loading" && <p role="status">{t("invitations:checking")}</p>}
      {read.kind === "closed" && <p role="alert">{t(uncertain ? "invitations:closedUnconfirmed" : "invitations:closed")}</p>}
      {read.kind === "failed" && <div role="alert"><p>{t(read.limited ? "invitations:limited" : "invitations:readFailed")}</p><Button type="button" variant="secondary" onClick={() => void refresh()}>{t("invitations:checkAgain")}</Button></div>}
      {invitation && <dl className="invitation-facts"><div><dt>{t("auth:email")}</dt><dd>{invitation.email}</dd></div><div><dt>{t("invitations:role")}</dt><dd>{t("auth:roles." + invitation.role, { defaultValue: t("auth:roles.unknown") })}</dd></div></dl>}
      {read.kind === "ready" && read.member && !uncertain && <section className="invitation-session"><h2>{t("invitations:alreadySignedIn")}</h2><p>{t("invitations:sessionBody", { email: read.member.email })}</p>
        <Button type="button" variant="secondary" busy={pending} busyLabel={t("invitations:signingOut")} onClick={() => void signOut()}>{t("invitations:signOut")}</Button></section>}
      {(issues.length > 0 || outcome !== null && outcome !== "closed") && <div ref={feedback} tabIndex={-1} role="alert" className="invitation-feedback">
        {issues.length > 0 ? <><p>{t("invitations:checkFields")}</p><ul>{issues.map(issue => <li key={issue}><button type="button" className="link-button" onClick={() => document.getElementById(issue === "passwordInvalid" ? "invitation-password" : "invitation-confirmation")?.focus()}>{t("invitations:" + issue, { defaultValue: t("invitations:checkFields") })}</button></li>)}</ul></>
          : outcome && <p>{t("invitations:" + outcome, { defaultValue: t("invitations:readFailed") })}</p>}
      </div>}
      {canAccept && <form className="invitation-form" aria-label={t("invitations:form")} aria-busy={pending} noValidate onSubmit={event => void accept(event)}>
        <Field ref={firstField} id="invitation-password" name="password" type={shown ? "text" : "password"} autoComplete="new-password" label={t("invitations:password")} hint={t("invitations:passwordHint")} value={password} disabled={pending}
          error={issues.includes("passwordInvalid") || outcome === "passwordInvalid" ? t("invitations:passwordInvalid") : undefined} onChange={event => { setPassword(event.target.value); setIssues([]); if (!uncertain) setOutcome(null); }} />
        <Field id="invitation-confirmation" name="confirmation" type={shown ? "text" : "password"} autoComplete="new-password" label={t("invitations:confirmation")} value={confirmation} disabled={pending}
          error={issues.includes("confirmationInvalid") ? t("invitations:confirmationInvalid") : undefined} onChange={event => { setConfirmation(event.target.value); setIssues([]); }} />
        <Button type="button" variant="quiet" aria-pressed={shown} disabled={pending} onClick={() => setShown(value => !value)}>{t(shown ? "invitations:hidePassword" : "invitations:showPassword")}</Button>
        <Button type="submit" busy={pending} busyLabel={t("invitations:accepting")}>{t("invitations:accept")}</Button>
      </form>}
      {uncertain && read.kind === "ready" && <><p>{t("invitations:uncertainOpen")}</p><Button type="button" variant="secondary" disabled={pending} onClick={() => void refresh()}>{t("invitations:checkAgain")}</Button></>}
      <a href={adminHomePath()}>{t("invitations:signInLink")}</a>
    </>}
    {outcome !== "accepted" && <p className="hint">{t("invitations:linkHint")}</p>}
  </main>;
}
