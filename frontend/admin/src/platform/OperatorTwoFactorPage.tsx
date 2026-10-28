import { useTranslation } from "react-i18next";
import { operatorTwoFactorApi } from "../api/operatorTwoFactor";
import type { PlatformUser } from "../api/platform";
import { TwoFactorPage, type TwoFactorContext } from "../components/auth/TwoFactorPage";
import { operatorPrefix } from "./routing";

export function OperatorTwoFactorPage({ user, sessionStale }: { user: PlatformUser; sessionStale: boolean }) {
  const { t } = useTranslation("operator");
  const context: TwoFactorContext = {
    realm: "operator", scopeKey: JSON.stringify([user.id.toLowerCase(), user.email.trim().toLowerCase()]), email: user.email,
    accountPath: "/platform/account", reloadPath: `${operatorPrefix()}/account/two-factor`,
    session: async signal => {
      const session = await operatorTwoFactorApi.session(signal);
      return { ...session, isCurrentAccount: user.id.toLowerCase() === session.id.toLowerCase() && user.email.trim().toLowerCase() === session.email.trim().toLowerCase() };
    },
    begin: operatorTwoFactorApi.begin, confirm: operatorTwoFactorApi.confirm, turnOff: operatorTwoFactorApi.turnOff,
  };
  return sessionStale ? <p role="status">{t("sessionStale")}</p> : <TwoFactorPage context={context} />;
}
