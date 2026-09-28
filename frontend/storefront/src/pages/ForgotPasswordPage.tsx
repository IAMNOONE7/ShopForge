import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { requestPasswordReset } from "../account";

export function ForgotPasswordPage() {
  const { t } = useTranslation("auth");
  const [sent, setSent] = useState(false);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    lock.current = true;
    setPending(true);
    const form = new FormData(event.currentTarget);
    await requestPasswordReset(String(form.get("email")).trim()).catch(
      () => undefined,
    );
    setSent(true);
    setPending(false);
    lock.current = false;
  }
  if (sent)
    return (
      <section className="account-form">
        <h1>{t("checkEmail")}</h1>
        <p>{t("resetEmailSent")}</p>
      </section>
    );
  return (
    <section className="account-form">
      <h1>{t("forgottenPassword")}</h1>
      <form onSubmit={(event) => void submit(event)} aria-busy={pending}>
        <label>
          {t("email")}{" "}
          <input name="email" type="email" autoComplete="email" required />
        </label>
        <button type="submit" disabled={pending}>
          {t("sendResetLink")}
        </button>
      </form>
    </section>
  );
}
