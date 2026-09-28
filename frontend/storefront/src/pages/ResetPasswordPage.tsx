import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useSearchParams } from "react-router";
import { resetPassword } from "../account";
import { RequestError } from "../components/ui/RequestError";

export function ResetPasswordPage() {
  const { t } = useTranslation(["auth", "validation", "errors"]);
  const [parameters] = useSearchParams();
  const navigate = useNavigate();
  const [problem, setProblem] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    lock.current = true;
    setPending(true);
    setProblem(null);
    const form = new FormData(event.currentTarget);
    try {
      await resetPassword(
        parameters.get("token") ?? "",
        String(form.get("password")),
      );
      void navigate("/account/sign-in", { replace: true });
    } catch (error) {
      setProblem(error);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }
  return (
    <section className="account-form">
      <h1>{t("auth:chooseNewPassword")}</h1>
      <form onSubmit={(event) => void submit(event)} aria-busy={pending}>
        <label>
          {t("auth:newPassword")}{" "}
          <input
            name="password"
            type="password"
            autoComplete="new-password"
            minLength={10}
            aria-describedby="reset-password-hint"
            required
          />
        </label>
        <span id="reset-password-hint" className="hint">
          {t("validation:passwordMinimum")}
        </span>
        <button type="submit" disabled={pending}>
          {t("auth:savePassword")}
        </button>
        {problem !== null && <RequestError error={problem} operation="write" />}
      </form>
    </section>
  );
}
