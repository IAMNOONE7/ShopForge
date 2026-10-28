import { sameStaffAccount } from "../api/staffAccount";
import { staffTwoFactorApi } from "../api/staffTwoFactor";
import { TwoFactorPage, type TwoFactorContext } from "../components/auth/TwoFactorPage";
import { useSession } from "../session";

export function StaffTwoFactorPage() {
  const { user } = useSession();
  const context: TwoFactorContext = {
    realm: "staff", scopeKey: JSON.stringify([user.id, user.tenantId, user.role, user.email.trim().toLowerCase()]), email: user.email,
    accountPath: "/account", reloadPath: `${import.meta.env.BASE_URL}account/two-factor`,
    session: async signal => { const session = await staffTwoFactorApi.session(signal); return { ...session, isCurrentAccount: sameStaffAccount(user, session) }; },
    begin: staffTwoFactorApi.begin, confirm: staffTwoFactorApi.confirm, turnOff: staffTwoFactorApi.turnOff,
  };
  return <TwoFactorPage context={context} />;
}
