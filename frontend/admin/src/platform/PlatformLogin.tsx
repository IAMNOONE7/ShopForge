import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { platformApi, type PlatformUser } from "../api/platform";
import { isHttpError } from "../api/http";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { InlineMessage } from "../components/ui/InlineMessage";
import { RequestError } from "../components/ui/RequestError";
import { operatorPrefix } from "./routing";

export function PlatformLogin({ expired, onSignedIn, onCheckSession }: { expired: boolean; onSignedIn: (user: PlatformUser) => void; onCheckSession: () => void }) {
  const { t } = useTranslation(["operator", "auth"]);
  const [email, setEmail] = useState(""), [password, setPassword] = useState(""), [code, setCode] = useState(""), [ticket, setTicket] = useState<string | null>(null);
  const [error, setError] = useState<unknown | null>(null), [pending, setPending] = useState(false);
  const lock = useRef(false), active = useRef(true), emailField = useRef<HTMLInputElement>(null), codeField = useRef<HTMLInputElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => { if (ticket) codeField.current?.focus(); else emailField.current?.focus(); }, [ticket]);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (lock.current || (!ticket && (!email.trim() || !password)) || ticket && !code.trim()) return;
    lock.current = true; setPending(true); setError(null); const sentPassword = password, sentCode = code.trim(); setPassword(""); setCode("");
    try {
      const result = ticket ? await platformApi.completeTwoFactor(ticket, sentCode) : await platformApi.login(email.trim(), sentPassword);
      if (!active.current) return;
      if (result.twoFactorRequired) setTicket(result.ticket); else { setTicket(null); onSignedIn(result.user); }
    } catch (failure) { if (active.current) setError(failure); }
    finally { lock.current = false; if (active.current) setPending(false); }
  }
  return <main id="operator-main" tabIndex={-1} className="login operator-login"><h1>{t("signInTitle")}</h1><p>{t("signInHint")}</p>
    {expired && <InlineMessage tone="error">{t("expired")}</InlineMessage>}
    <form onSubmit={submit} aria-busy={pending}>
      <fieldset className="operator-fields" disabled={pending}><legend className="sr-only">{t(ticket ? "codeTitle" : "signInTitle")}</legend>
        {ticket ? <><p>{t("codeHint")}</p><Field id="operator-code" name="code" label={t("code")} value={code} autoComplete="one-time-code" required
          ref={codeField} onChange={event => setCode(event.target.value)} /></> : <>
          <Field id="operator-email" name="email" type="email" label={t("auth:email")} autoComplete="username" required value={email} ref={emailField} onChange={event => setEmail(event.target.value)} />
          <Field id="operator-password" name="password" type="password" label={t("auth:password")} autoComplete="current-password" required value={password} onChange={event => setPassword(event.target.value)} />
        </>}
      </fieldset>
      <div className="cluster"><Button type="submit" busy={pending} busyLabel={t(ticket ? "auth:verifying" : "auth:signingIn")}>{t(ticket ? "auth:verify" : "auth:signIn")}</Button>
        {ticket && <Button type="button" variant="secondary" disabled={pending} onClick={() => { setTicket(null); setPassword(""); setCode(""); setError(null); }}>{t("restart")}</Button>}</div>
    </form>
    <p><a href={`${operatorPrefix()}/forgot-password`}>{t("forgotPassword")}</a></p>
    {error !== null && (ticket && isHttpError(error) && error.status === 401 ? <InlineMessage tone="error">{t("wrongCode")}</InlineMessage> : <RequestError error={error} operation="login" />)}
    {error !== null && <div><p>{t("checkSessionHint")}</p><Button type="button" variant="secondary" disabled={pending} onClick={onCheckSession}>{t("checkSession")}</Button></div>}
  </main>;
}
