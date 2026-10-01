import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useSearchParams } from "react-router";
import { signIn } from "../account";
import { authPath, clearAuthReturn, resolveAuthReturn, signInIssues, type AuthIssue } from "../auth";
import { AuthLayout } from "../components/auth/AuthLayout";
import { AuthValidationSummary } from "../components/auth/AuthValidationSummary";
import { PasswordField } from "../components/auth/PasswordField";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";

export function SignInPage() {
  const { t } = useTranslation(["auth", "errors", "validation"]);
  const navigate = useNavigate();
  const [parameters] = useSearchParams();
  const { apply } = useCustomer();
  const [problem, setProblem] = useState<unknown | null>(null);
  const [issues, setIssues] = useState<AuthIssue[]>([]);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  const returnTo = resolveAuthReturn(parameters.get("returnTo"));

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    const form = event.currentTarget;
    const email = String(new FormData(form).get("email")).trim();
    const password = String(new FormData(form).get("password"));
    const invalid = signInIssues({ email, password });
    setIssues(invalid);
    if (invalid.length > 0) return;

    lock.current = true;
    setProblem(null);
    setPending(true);
    try {
      apply(await signIn(email, password));
      clearAuthReturn();
      void navigate(returnTo, { replace: true });
    } catch (error) {
      setProblem(error);
      const secret = form.elements.namedItem("password");
      if (secret instanceof HTMLInputElement) secret.value = "";
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  return (
    <AuthLayout
      title={t("auth:signIn")}
      footer={
        <p>
          <Link to={authPath("/account/register", returnTo)}>
            {t("auth:createAccount")}
          </Link>{" "}
          ·{" "}
          <Link to={authPath("/account/forgot-password", returnTo)}>
            {t("auth:forgottenPassword")}
          </Link>
        </p>
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
        <PasswordField
          label={t("auth:password")}
          autoComplete="current-password"
          disabled={pending}
          invalid={issues.some((issue) => issue.field === "password")}
        />
        <Button type="submit" busy={pending} busyLabel={t("auth:signingIn")}>
          {t("auth:signIn")}
        </Button>
        {problem !== null && <RequestError error={problem} operation="login" />}
      </form>
    </AuthLayout>
  );
}
