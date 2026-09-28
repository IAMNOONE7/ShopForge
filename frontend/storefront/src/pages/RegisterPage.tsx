import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { register } from "../account";
import { RequestError } from "../components/ui/RequestError";

export function RegisterPage() {
  const { t } = useTranslation(["auth", "validation", "errors"]);
  const [sent, setSent] = useState(false);
  const [failed, setFailed] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    lock.current = true;
    setPending(true);
    setFailed(null);
    const form = new FormData(event.currentTarget);
    try {
      const phone = String(form.get("phone")).trim();
      await register({
        email: String(form.get("email")).trim(),
        password: String(form.get("password")),
        firstName: String(form.get("firstName")).trim(),
        lastName: String(form.get("lastName")).trim(),
        phone: phone || null,
      });
      setSent(true);
    } catch (error) {
      setFailed(error);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }
  if (sent)
    return (
      <section className="account-form">
        <h1>{t("auth:checkEmail")}</h1>
        <p>{t("auth:registrationEmailSent")}</p>
      </section>
    );
  return (
    <section className="account-form">
      <h1>{t("auth:createAccount")}</h1>
      <form onSubmit={(event) => void submit(event)} aria-busy={pending}>
        <label>
          {t("auth:firstName")}{" "}
          <input name="firstName" autoComplete="given-name" required />
        </label>
        <label>
          {t("auth:lastName")}{" "}
          <input name="lastName" autoComplete="family-name" required />
        </label>
        <label>
          {t("auth:email")}{" "}
          <input name="email" type="email" autoComplete="email" required />
        </label>
        <label>
          {t("auth:phoneOptional")}{" "}
          <input name="phone" type="tel" autoComplete="tel" />
        </label>
        <label>
          {t("auth:password")}{" "}
          <input
            name="password"
            type="password"
            autoComplete="new-password"
            minLength={10}
            aria-describedby="register-password-hint"
            required
          />
        </label>
        <span id="register-password-hint" className="hint">
          {t("validation:passwordMinimum")}
        </span>
        <button type="submit" disabled={pending}>
          {t("auth:createAccountAction")}
        </button>
        {failed !== null && <RequestError error={failed} operation="write" />}
      </form>
      <p className="hint">
        {t("auth:alreadyRegistered")}{" "}
        <Link to="/account/sign-in">{t("auth:signIn")}</Link>
      </p>
    </section>
  );
}
