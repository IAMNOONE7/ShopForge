import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";
import { customerEmailApi } from "../api/customerEmail";
import { isHttpError } from "../api/http";
import { authPath, emailIssues, rememberAuthReturn, safeAuthReturn, type AuthIssue } from "../auth";
import { AuthLayout } from "../components/auth/AuthLayout";
import { AuthValidationSummary } from "../components/auth/AuthValidationSummary";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { RequestError } from "../components/ui/RequestError";
import { useStore } from "../storeContext";
import "./CustomerEmail.css";

export function ResendVerificationPage() {
  const store = useStore();
  return <ResendForm key={store.id} />;
}
function ResendForm() {
  const { t } = useTranslation(["customerEmail", "auth", "validation"]);
  const [params] = useSearchParams(), returnTo = safeAuthReturn(params.get("returnTo"));
  const [email, setEmail] = useState(""), [issues, setIssues] = useState<AuthIssue[]>([]), [error, setError] = useState<unknown | null>(null);
  const [phase, setPhase] = useState<"editing" | "accepted" | "uncertain">("editing"), [pending, setPending] = useState(false);
  const active = useRef(false), lock = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (error !== null || phase !== "editing") feedback.current?.focus(); }, [error, phase]);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (lock.current || phase !== "editing") return;
    const invalid = emailIssues(email); setIssues(invalid); if (invalid.length) return;
    lock.current = true; setPending(true); setError(null); const request = new AbortController(); controller.current = request;
    try {
      await customerEmailApi.resend(email.trim(), request.signal);
      if (active.current && !request.signal.aborted) { rememberAuthReturn(returnTo); setPhase("accepted"); }
    } catch (failure) {
      if (!active.current || request.signal.aborted) return;
      setError(failure);
      if (!isHttpError(failure) || failure.kind !== "http" || (failure.status ?? 500) >= 500) setPhase("uncertain");
    } finally { lock.current = false; if (active.current) setPending(false); }
  }
  return <AuthLayout className="customer-email-page" title={t("customerEmail:resendTitle")} introduction={<p>{t("customerEmail:resendIntro")}</p>} footer={<Link to={authPath("/account/sign-in", returnTo)}>{t("auth:backToSignIn")}</Link>}>
    <div className="customer-email">
      {phase !== "accepted" && <form noValidate onSubmit={event => void submit(event)} aria-busy={pending || undefined}>
        <AuthValidationSummary issues={issues} />
        <Field name="email" type="email" autoComplete="email" label={t("auth:email")} value={email} required disabled={pending || phase === "uncertain"} error={issues.length ? t("validation:emailInvalid") : undefined} onChange={event => { setEmail(event.target.value); setIssues([]); }} />
        <Button type="submit" disabled={phase === "uncertain"} busy={pending} busyLabel={t("customerEmail:sending")}>{t("customerEmail:resendAction")}</Button>
      </form>}
      {(error !== null || phase !== "editing") && <div ref={feedback} tabIndex={-1} className="customer-email-feedback">
        {phase === "accepted" && <p role="status">{t("customerEmail:resendAccepted")}</p>}
        {phase === "uncertain" && <p role="status">{t("customerEmail:resendUncertain")}</p>}
        {error !== null && <RequestError error={error} operation="write" />}
      </div>}
      <p className="hint">{t("customerEmail:newestVerification")}</p>
    </div>
  </AuthLayout>;
}
