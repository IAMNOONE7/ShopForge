import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useSearchParams } from "react-router";
import { resetPassword } from "../account";
import { authPath, passwordIssues, resolveAuthReturn, type AuthIssue } from "../auth";
import { statusOf } from "../api/errors";
import { AuthLayout } from "../components/auth/AuthLayout";
import { AuthValidationSummary } from "../components/auth/AuthValidationSummary";
import { PasswordField } from "../components/auth/PasswordField";
import { Button } from "../components/ui/Button";
import { RequestError } from "../components/ui/RequestError";

export function ResetPasswordPage() {
  const { t } = useTranslation(["auth", "validation"]);
  const [parameters] = useSearchParams();
  const token = parameters.get("token")?.trim() ?? "";
  const returnTo = resolveAuthReturn(parameters.get("returnTo"));
  const [problem, setProblem] = useState<unknown | null>(null);
  const [issues, setIssues] = useState<AuthIssue[]>([]);
  const [pending, setPending] = useState(false);
  const [invalidToken, setInvalidToken] = useState(token.length === 0);
  const [changed, setChanged] = useState(false);
  const lock = useRef(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    const element = event.currentTarget;
    const password = String(new FormData(element).get("password"));
    const invalid = passwordIssues(password);
    setIssues(invalid);
    if (invalid.length > 0) return;

    lock.current = true;
    setPending(true);
    setProblem(null);
    try {
      await resetPassword(token, password);
      setChanged(true);
    } catch (error) {
      if (statusOf(error) === 400) setInvalidToken(true);
      else setProblem(error);
      const secret = element.elements.namedItem("password");
      if (secret instanceof HTMLInputElement) secret.value = "";
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  if (invalidToken)
    return (
      <InvalidToken
        returnTo={returnTo}
        title={t("auth:expiredLinkTitle")}
        body={t("auth:expiredLinkBody")}
        action={t("auth:requestNewReset")}
      />
    );

  if (changed)
    return (
      <AuthLayout
        title={t("auth:passwordChangedTitle")}
        footer={
          <Link className="button" to={authPath("/account/sign-in", returnTo)}>
            {t("auth:signIn")}
          </Link>
        }
      >
        <p role="status">{t("auth:passwordChangedBody")}</p>
      </AuthLayout>
    );

  return (
    <AuthLayout title={t("auth:chooseNewPassword")}>
      <form onSubmit={(event) => void submit(event)} aria-busy={pending} noValidate>
        <AuthValidationSummary issues={issues} />
        <PasswordField
          label={t("auth:newPassword")}
          autoComplete="new-password"
          hint={t("validation:passwordMinimum")}
          disabled={pending}
          invalid={issues.length > 0}
        />
        <Button type="submit" busy={pending} busyLabel={t("auth:savingPassword")}>
          {t("auth:savePassword")}
        </Button>
        {problem !== null && <RequestError error={problem} operation="write" />}
      </form>
    </AuthLayout>
  );
}

function InvalidToken({
  returnTo,
  title,
  body,
  action,
}: {
  returnTo: string;
  title: string;
  body: string;
  action: string;
}) {
  const { t } = useTranslation("auth");
  return (
    <AuthLayout title={title} footer={<Link to={authPath("/account/sign-in", returnTo)}>{t("signIn")}</Link>}>
      <p>{body}</p>
      <Link className="button" to={authPath("/account/forgot-password", returnTo)}>
        {action}
      </Link>
    </AuthLayout>
  );
}
