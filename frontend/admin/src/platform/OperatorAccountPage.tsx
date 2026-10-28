import { useTranslation } from "react-i18next";
import { operatorAccountApi, sameOperatorAccount } from "../api/operatorAccount";
import type { PlatformUser } from "../api/platform";
import { AccountSecurityPage, type AccountSecurityContext } from "../components/auth/AccountSecurityPage";
import { operatorPrefix } from "./routing";

export function OperatorAccountPage({ user, sessionStale }: { user: PlatformUser; sessionStale: boolean }) {
  const { t } = useTranslation("operator");
  const context: AccountSecurityContext = {
    realm: "operator", scopeKey: JSON.stringify([user.id.toLowerCase(), user.email.trim().toLowerCase(), user.isTwoFactorEnabled]), email: user.email,
    reloadPath: `${operatorPrefix()}/account`, recoveryPath: `${operatorPrefix()}/forgot-password`, twoFactorPath: "/platform/account/two-factor",
    check: async signal => sameOperatorAccount(user, await operatorAccountApi.session(signal)),
    password: operatorAccountApi.password, endOtherSessions: operatorAccountApi.endOtherSessions,
  };
  return sessionStale ? <p role="status">{t("sessionStale")}</p> : <AccountSecurityPage context={context} />;
}
