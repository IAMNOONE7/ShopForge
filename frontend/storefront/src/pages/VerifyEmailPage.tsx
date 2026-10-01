import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useSearchParams } from "react-router";
import { verifyEmail } from "../account";
import { authPath, clearAuthReturn, resolveAuthReturn } from "../auth";
import { statusOf } from "../api/errors";
import { AuthLayout } from "../components/auth/AuthLayout";
import { Button } from "../components/ui/Button";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";

export function VerifyEmailPage() {
  const { t } = useTranslation("auth");
  const [parameters] = useSearchParams();
  const navigate = useNavigate();
  const { apply } = useCustomer();
  const token = parameters.get("token")?.trim() ?? "";
  const returnTo = resolveAuthReturn(parameters.get("returnTo"));
  const [invalid, setInvalid] = useState(token.length === 0);
  const [problem, setProblem] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);

  async function confirm() {
    if (lock.current || !token) return;
    lock.current = true;
    setPending(true);
    setProblem(null);
    try {
      const customer = await verifyEmail(token);
      apply(customer);
      clearAuthReturn();
      void navigate(returnTo, { replace: true });
    } catch (error) {
      if (statusOf(error) === 400) setInvalid(true);
      else setProblem(error);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  if (invalid)
    return (
      <AuthLayout
        title={t("expiredLinkTitle")}
        footer={
          <p>
            <Link to={authPath("/account/register", returnTo)}>{t("registerAgain")}</Link>{" "}
            {t("registerAgainSuffix")}
          </p>
        }
      >
        <p>{t("expiredLinkBody")}</p>
      </AuthLayout>
    );

  return (
    <AuthLayout
      title={t("confirmEmailTitle")}
      introduction={<p>{t("confirmEmailBody")}</p>}
    >
      <Button
        type="button"
        busy={pending}
        busyLabel={t("confirmingEmail")}
        onClick={() => void confirm()}
      >
        {t("confirmEmailAction")}
      </Button>
      {problem !== null && <RequestError error={problem} operation="write" />}
    </AuthLayout>
  );
}
