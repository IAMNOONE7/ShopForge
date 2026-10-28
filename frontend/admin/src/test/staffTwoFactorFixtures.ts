import type { StaffSecuritySession } from "../api/staffTwoFactor";

export const securityStaff: StaffSecuritySession = { id: "staff-a", tenantId: "company-a", email: "staff@example.test", role: "Support", isTwoFactorEnabled: false };
export const twoFactorSetup = {
  secret: "A".repeat(32),
  enrolmentUri: `otpauth://totp/ShopForge:${encodeURIComponent(securityStaff.email)}?secret=${"A".repeat(32)}&issuer=ShopForge&digits=6&period=30`,
};
export const recoveryCodes = Array.from({ length: 10 }, (_, index) => `${(index + 1).toString(16).toUpperCase().padStart(6, "0")}-ABCDEF`);
