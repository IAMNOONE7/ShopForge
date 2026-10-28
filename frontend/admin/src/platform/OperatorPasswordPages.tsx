import { useTranslation } from "react-i18next";
import { operatorRecoveryApi } from "../api/operatorRecovery";
import { ForgotPasswordPage, ResetPasswordPage, type RecoveryContext } from "../components/auth/PasswordRecoveryPages";
import type { OperatorResetProof } from "./operatorRecoveryEntry";
import { operatorHomePath } from "./routing";

function useRecoveryContext(): RecoveryContext {
  const { t } = useTranslation("operator");
  return { realm: "operator", brand: t("title"), api: operatorRecoveryApi, homePath: operatorHomePath(), requestPath: "/platform/forgot-password" };
}
export function ForgotOperatorPasswordPage() { return <ForgotPasswordPage context={useRecoveryContext()} />; }
export function ResetOperatorPasswordPage({ proof }: { proof: OperatorResetProof | null }) { return <ResetPasswordPage context={useRecoveryContext()} proof={proof} />; }
