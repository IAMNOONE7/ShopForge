import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useSearchParams } from "react-router";
import { register } from "../account";
import { authPath, registrationIssues, rememberAuthReturn, safeAuthReturn, type AuthIssue } from "../auth";
import { AuthLayout } from "../components/auth/AuthLayout";
import { AuthValidationSummary } from "../components/auth/AuthValidationSummary";
import { PasswordField } from "../components/auth/PasswordField";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { RequestError } from "../components/ui/RequestError";

export function RegisterPage() {
  const { t } = useTranslation(["auth", "validation", "errors"]);
  const [parameters] = useSearchParams();
  const [sent, setSent] = useState(false);
  const [failed, setFailed] = useState<unknown | null>(null);
  const [issues, setIssues] = useState<AuthIssue[]>([]);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  const returnTo = safeAuthReturn(parameters.get("returnTo"));

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    const element = event.currentTarget;
    const form = new FormData(element);
    const draft = {
      email: String(form.get("email")).trim(),
      password: String(form.get("password")),
      firstName: String(form.get("firstName")).trim(),
      lastName: String(form.get("lastName")).trim(),
    };
    const invalid = registrationIssues(draft);
    setIssues(invalid);
    if (invalid.length > 0) return;

    lock.current = true;
    setPending(true);
    setFailed(null);
    try {
      const phone = String(form.get("phone")).trim();
      await register({ ...draft, phone: phone || null });
      rememberAuthReturn(returnTo);
      setSent(true);
    } catch (error) {
      setFailed(error);
      const secret = element.elements.namedItem("password");
      if (secret instanceof HTMLInputElement) secret.value = "";
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
        <p role="status">{t("auth:registrationEmailSent")}</p>
      </AuthLayout>
    );

  return (
    <AuthLayout
      title={t("auth:createAccount")}
      footer={
        <p>
          {t("auth:alreadyRegistered")} {" "}
          <Link to={authPath("/account/sign-in", returnTo)}>{t("auth:signIn")}</Link>
        </p>
      }
    >
      <form onSubmit={(event) => void submit(event)} aria-busy={pending} noValidate>
        <AuthValidationSummary issues={issues} />
        <Field
          label={t("auth:firstName")}
          name="firstName"
          autoComplete="given-name"
          maxLength={100}
          required
          disabled={pending}
          error={issues.some((issue) => issue.field === "firstName") ? t("validation:nameInvalid") : null}
        />
        <Field
          label={t("auth:lastName")}
          name="lastName"
          autoComplete="family-name"
          maxLength={100}
          required
          disabled={pending}
          error={issues.some((issue) => issue.field === "lastName") ? t("validation:nameInvalid") : null}
        />
        <Field
          label={t("auth:email")}
          name="email"
          type="email"
          autoComplete="username"
          required
          disabled={pending}
          error={issues.some((issue) => issue.field === "email") ? t("validation:emailInvalid") : null}
        />
        <Field
          label={t("auth:phoneOptional")}
          name="phone"
          type="tel"
          autoComplete="tel"
          disabled={pending}
        />
        <PasswordField
          label={t("auth:password")}
          autoComplete="new-password"
          hint={t("validation:passwordMinimum")}
          disabled={pending}
          invalid={issues.some((issue) => issue.field === "password")}
        />
        <Button type="submit" busy={pending} busyLabel={t("auth:creatingAccount")}>
          {t("auth:createAccountAction")}
        </Button>
        {failed !== null && <RequestError error={failed} operation="write" />}
      </form>
    </AuthLayout>
  );
}
