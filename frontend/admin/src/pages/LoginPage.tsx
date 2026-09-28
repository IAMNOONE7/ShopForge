import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type CurrentUser } from "../api";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { InlineMessage } from "../components/ui/InlineMessage";
import { RequestError } from "../components/ui/RequestError";

export function LoginPage({
  expired,
  onLogin,
}: {
  expired: boolean;
  onLogin: (user: CurrentUser) => void;
}) {
  const { t } = useTranslation(["auth", "errors"]);
  const [error, setError] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);

  async function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    lock.current = true;
    setError(null);
    setPending(true);
    const form = new FormData(event.currentTarget);
    try {
      onLogin(
        await api.login(
          String(form.get("email")),
          String(form.get("password")),
        ),
      );
    } catch (exception) {
      setError(exception);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  return (
    <main className="login">
      <h1>{t("auth:title")}</h1>
      {expired && (
        <InlineMessage tone="error">{t("errors:sessionExpired")}</InlineMessage>
      )}
      <form
        onSubmit={(event) => void login(event)}
        className="stack"
        aria-busy={pending}
      >
        <Field
          label={t("auth:email")}
          name="email"
          type="email"
          autoComplete="username"
          required
        />
        <Field
          label={t("auth:password")}
          name="password"
          type="password"
          autoComplete="current-password"
          required
        />
        <Button type="submit" busy={pending} busyLabel={t("auth:signingIn")}>
          {t("auth:signIn")}
        </Button>
        {error !== null && <RequestError error={error} operation="login" />}
      </form>
    </main>
  );
}
