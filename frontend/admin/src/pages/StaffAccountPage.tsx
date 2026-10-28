import { sameStaffAccount, staffAccountApi } from "../api/staffAccount";
import { AccountSecurityPage, type AccountSecurityContext } from "../components/auth/AccountSecurityPage";
import { useSession } from "../session";

export function StaffAccountPage() {
  const { user } = useSession();
  const context: AccountSecurityContext = {
    realm: "staff", scopeKey: JSON.stringify([user.id, user.tenantId, user.role, user.email.trim().toLowerCase()]), email: user.email,
    reloadPath: `${import.meta.env.BASE_URL}account`, recoveryPath: `${import.meta.env.BASE_URL}forgot-password`, twoFactorPath: "/account/two-factor",
    check: async signal => sameStaffAccount(user, await staffAccountApi.session(signal)),
    password: staffAccountApi.password, endOtherSessions: staffAccountApi.endOtherSessions,
  };
  return <AccountSecurityPage context={context} />;
}
