import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useSearchParams } from "react-router";
import { requestPasswordReset } from "../account";
import { authPath, emailIssues, rememberAuthReturn, safeAuthReturn, type AuthIssue } from "../auth";
import { AuthLayout } from "../components/auth/AuthLayout";
import { AuthValidationSummary } from "../components/auth/AuthValidationSummary";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { RequestError } from "../components/ui/RequestError";

export function ForgotPasswordPage() {
  const { t } = useTranslation(["auth", "validation"]);
  const [parameters] = useSearchParams();
  const [sent, setSent] = useState(false);
  const [problem, setProblem] = useState<unknown | null>(null);
  const [issues, setIssues] = useState<AuthIssue[]>([]);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  const returnTo = safeAuthReturn(parameters.get("returnTo"));

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    const email = String(new FormData(event.currentTarget).get("email")).trim();
    const invalid = emailIssues(email);
    setIssues(invalid);
    if (invalid.length > 0) return;

    lock.current = true;
    setProblem(null);
    setPending(true);
    try {
      await requestPasswordReset(email);
      rememberAuthReturn(returnTo);
      setSent(true);
    } catch (error) {
      setProblem(error);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  if (sent)
    return (
      <AuthLayout
        title={t("auth:checkEmail")}
        footer={
          <Link to={authPath("/account/sign-in", returnTo)}>
            {t("auth:backToSignIn")}
          </Link>
        }
      >
        <p role="status">{t("auth:resetEmailSent")}</p>
      </AuthLayout>
    );

  return (
    <AuthLayout
      title={t("auth:forgottenPassword")}
      introduction={<p>{t("auth:forgotPasswordIntro")}</p>}
      footer={
        <Link to={authPath("/account/sign-in", returnTo)}>
          {t("auth:backToSignIn")}
        </Link>
      }
    >
      <form onSubmit={(event) => void submit(event)} aria-busy={pending} noValidate>
        <AuthValidationSummary issues={issues} />
        <Field
          label={t("auth:email")}
          name="email"
          type="email"
          autoComplete="username"
          required
          disabled={pending}
          error={issues.some((issue) => issue.field === "email") ? t("validation:emailInvalid") : null}
        />
        <Button type="submit" busy={pending} busyLabel={t("auth:sendingResetLink")}>
          {t("auth:sendResetLink")}
        </Button>
        {problem !== null && <RequestError error={problem} operation="write" />}
      </form>
    </AuthLayout>
  );
}
