import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { signIn } from "../account";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";

export function SignInPage() {
  const { t } = useTranslation(["auth", "errors"]);
  const navigate = useNavigate();
  const { apply } = useCustomer();
  const [problem, setProblem] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    lock.current = true;
    setProblem(null);
    setPending(true);
    const form = new FormData(event.currentTarget);
    try {
      apply(
        await signIn(
          String(form.get("email")).trim(),
          String(form.get("password")),
        ),
      );
      void navigate("/account");
    } catch (error) {
      setProblem(error);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }
  return (
    <section className="account-form">
      <h1>{t("signIn")}</h1>
      <form onSubmit={(event) => void submit(event)} aria-busy={pending}>
        <Field
          label={t("email")}
          name="email"
          type="email"
          autoComplete="email"
          required
        />
        <Field
          label={t("password")}
          name="password"
          type="password"
          autoComplete="current-password"
          required
        />
        <Button type="submit" busy={pending} busyLabel={t("signingIn")}>
          {t("signIn")}
        </Button>
        {problem !== null && <RequestError error={problem} operation="login" />}
      </form>
      <p className="hint">
        <Link to="/account/register">{t("createAccount")}</Link> ·{" "}
        <Link to="/account/forgot-password">{t("forgottenPassword")}</Link>
      </p>
    </section>
  );
}
