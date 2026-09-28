import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useSearchParams } from "react-router";
import { verifyEmail } from "../account";
import { Message } from "../components/Message";
import { LoadingState } from "../components/ui/LoadingState";
import { useCustomer } from "../customerContext";

export function VerifyEmailPage() {
  const { t } = useTranslation(["auth", "common"]);
  const [parameters] = useSearchParams();
  const navigate = useNavigate();
  const { apply } = useCustomer();
  const [failed, setFailed] = useState(false);
  const token = parameters.get("token") ?? "";
  useEffect(() => {
    verifyEmail(token)
      .then((customer) => {
        apply(customer);
        void navigate("/account", { replace: true });
      })
      .catch(() => setFailed(true));
  }, [token]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!failed) return <LoadingState label={t("common:loading")} lines={4} />;
  return (
    <>
      <Message
        title={t("auth:expiredLinkTitle")}
        text={t("auth:expiredLinkBody")}
      />
      <p className="hint">
        <Link to="/account/register">{t("auth:registerAgain")}</Link>{" "}
        {t("auth:registerAgainSuffix")}
      </p>
    </>
  );
}
