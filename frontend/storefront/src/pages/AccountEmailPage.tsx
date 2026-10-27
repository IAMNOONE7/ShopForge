import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import type { Customer } from "../account";
import { customerEmailApi } from "../api/customerEmail";
import { isHttpError } from "../api/http";
import { statusOf } from "../api/errors";
import { authPath, signInIssues, type AuthIssue } from "../auth";
import { AuthLayout } from "../components/auth/AuthLayout";
import { AuthValidationSummary } from "../components/auth/AuthValidationSummary";
import { PasswordField } from "../components/auth/PasswordField";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";
import { useStore } from "../storeContext";
import "./CustomerEmail.css";

export function AccountEmailPage() {
  const session = useCustomer(), store = useStore(), { t } = useTranslation("customerEmail");
  const [accountChanged, setAccountChanged] = useState(false);
  if (session.status === "checking") return <AuthLayout className="customer-email-page" title={t("changeTitle")}><LoadingState label={t("checkingSession")} /></AuthLayout>;
  if (session.status === "error") return <AuthLayout className="customer-email-page" title={t("changeTitle")}><RequestError error={session.error} operation="session" onRetry={session.retry} /></AuthLayout>;
  if (!session.customer || session.status !== "authenticated") return <AuthLayout className="customer-email-page" title={t("changeTitle")}><p>{t("signInRequired")}</p><Link to={authPath("/account/sign-in", "/account/email")}>{t("signIn")}</Link></AuthLayout>;
  return <EmailChangeForm key={JSON.stringify([store.id, session.customer.email])} customer={session.customer} accountChanged={accountChanged} onDifferent={customer => { setAccountChanged(true); session.apply(customer); }} onExpired={() => session.apply(null)} />;
}
function EmailChangeForm({ customer, accountChanged, onDifferent, onExpired }: { customer: Customer; accountChanged: boolean; onDifferent: (customer: Customer) => void; onExpired: () => void }) {
  const { t } = useTranslation(["customerEmail", "auth", "validation"]);
  const [email, setEmail] = useState(""), [issues, setIssues] = useState<AuthIssue[]>([]), [failure, setFailure] = useState<{ error: unknown; submitted: boolean } | null>(null);
  const [phase, setPhase] = useState<"editing" | "accepted" | "uncertain">("editing"), [pending, setPending] = useState(false), [attempt, setAttempt] = useState(0);
  const active = useRef(false), lock = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (failure || phase !== "editing") feedback.current?.focus(); }, [failure, phase]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (lock.current || phase !== "editing") return;
    const password = String(new FormData(event.currentTarget).get("password") ?? "");
    const invalid = signInIssues({ email, password }); setIssues(invalid); if (invalid.length) return;
    const secret = event.currentTarget.elements.namedItem("password"); if (secret instanceof HTMLInputElement) secret.value = "";
    setAttempt(value => value + 1); lock.current = true; setPending(true); setFailure(null);
    const request = new AbortController(); controller.current = request; let submitted = false;
    try {
      const current = await customerEmailApi.profile(request.signal);
      if (!active.current || request.signal.aborted) return;
      if (current.email !== customer.email) { onDifferent(current); return; }
      submitted = true;
      await customerEmailApi.request(email.trim(), password, request.signal);
      if (active.current && !request.signal.aborted) setPhase("accepted");
    } catch (error) {
      if (!active.current || request.signal.aborted) return;
      if (statusOf(error) === 401) { onExpired(); return; }
      setFailure({ error, submitted });
      if (submitted && (!isHttpError(error) || error.kind !== "http" || (error.status ?? 500) >= 500)) setPhase("uncertain");
    } finally { lock.current = false; if (active.current) setPending(false); }
  }
  const wrongPassword = failure?.submitted && statusOf(failure.error) === 403;
  return <AuthLayout className="customer-email-page" title={t("customerEmail:changeTitle")} introduction={<p>{t("customerEmail:changeIntro")}</p>} footer={<Link to="/account">{t("customerEmail:backAccount")}</Link>}>
    <div className="customer-email"><p>{t("customerEmail:currentAddress", { email: customer.email })}</p>
      {accountChanged && <p role="status">{t("customerEmail:accountChanged")}</p>}
      {phase !== "accepted" && <form noValidate onSubmit={event => void submit(event)} aria-busy={pending || undefined}>
        <AuthValidationSummary issues={issues} />
        <Field name="email" type="email" autoComplete="email" label={t("customerEmail:newAddress")} value={email} required disabled={pending || phase === "uncertain"} error={issues.some(issue => issue.field === "email") ? t("validation:emailInvalid") : undefined} onChange={event => { setEmail(event.target.value); setIssues(current => current.filter(issue => issue.field !== "email")); }} />
        <PasswordField key={attempt} name="password" label={t("customerEmail:currentPassword")} autoComplete="current-password" hint={t("customerEmail:passwordHint")} disabled={pending || phase === "uncertain"} invalid={Boolean(wrongPassword || issues.some(issue => issue.field === "password"))} />
        <Button type="submit" disabled={phase === "uncertain"} busy={pending} busyLabel={t("customerEmail:sending")}>{t("customerEmail:requestChange")}</Button>
      </form>}
      {(failure || phase !== "editing") && <div ref={feedback} tabIndex={-1} className="customer-email-feedback">
        {phase === "accepted" && <p role="status">{t("customerEmail:changeAccepted")}</p>}
        {phase === "uncertain" && <p role="status">{t("customerEmail:changeUncertain")}</p>}
        {wrongPassword ? <p role="alert">{t("customerEmail:wrongPassword")}</p> : failure && <><RequestError error={failure.error} operation={failure.submitted ? "write" : "read"} fieldTargets={{ newEmail: "email" }} />{!failure.submitted && <p>{t("customerEmail:sessionReadFailed")}</p>}</>}
      </div>}
      <p className="hint">{t("customerEmail:newestChange")}</p>
    </div>
  </AuthLayout>;
}
