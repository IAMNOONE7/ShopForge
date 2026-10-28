import { ForgotPasswordPage, ResetPasswordPage, type RecoveryContext } from "../components/auth/PasswordRecoveryPages";
import { staffRecoveryApi } from "../api/staffRecovery";
import { adminHomePath } from "../routing";
import type { StaffResetProof } from "../staffRecoveryEntry";

const context: RecoveryContext = { realm: "staff", brand: "ShopForge Admin", api: staffRecoveryApi, homePath: adminHomePath(), requestPath: "/forgot-password" };
export function ForgotStaffPasswordPage() { return <ForgotPasswordPage context={context} />; }
export function ResetStaffPasswordPage({ proof }: { proof: StaffResetProof | null }) { return <ResetPasswordPage context={context} proof={proof} />; }
