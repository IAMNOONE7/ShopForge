import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type CurrentUser } from "../api";
import { statusOf } from "../api/errors";
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
  const [ticket, setTicket] = useState<string | null>(null);
  const lock = useRef(false);

  async function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    lock.current = true;
    setError(null);
    setPending(true);
    const form = new FormData(event.currentTarget);
    try {
      const result = ticket
        ? await api.completeTwoFactor(ticket, String(form.get("code")).trim())
        : await api.login(
            String(form.get("email")),
            String(form.get("password")),
          );
      if (result.user) onLogin(result.user);
      else if (result.twoFactorRequired && result.ticket) setTicket(result.ticket);
      else throw new Error("Invalid sign-in response.");
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
      {ticket && <p className="hint">{t("auth:codePrompt")}</p>}
      <form
        onSubmit={(event) => void login(event)}
        className="stack"
        aria-busy={pending}
      >
        {ticket ? (
          <Field
            label={t("auth:code")}
            name="code"
            autoComplete="one-time-code"
            required
            autoFocus
          />
        ) : (
          <>
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
          </>
        )}
        <Button type="submit" busy={pending} busyLabel={t(ticket ? "auth:verifying" : "auth:signingIn")}>
          {t(ticket ? "auth:verify" : "auth:signIn")}
        </Button>
        {ticket && (
          <Button type="button" variant="secondary" disabled={pending} onClick={() => {
            setTicket(null);
            setError(null);
          }}>
            {t("auth:differentAccount")}
          </Button>
        )}
        {error !== null && ticket && statusOf(error) === 401
          ? <InlineMessage tone="error">{t("auth:invalidCode")}</InlineMessage>
          : error !== null && <RequestError error={error} operation="login" />}
      </form>
    </main>
  );
}
