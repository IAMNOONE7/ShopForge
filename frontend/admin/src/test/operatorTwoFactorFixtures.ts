import type { PlatformUser } from "../api/platform";

export const securityOperator: PlatformUser = { id: "00000000-0000-0000-0000-000000000001", email: "operator@example.test", isTwoFactorEnabled: false };
export const twoFactorSetup = {
  secret: "A".repeat(32),
  enrolmentUri: `otpauth://totp/ShopForge%20Platform:${encodeURIComponent(securityOperator.email)}?secret=${"A".repeat(32)}&issuer=ShopForge%20Platform&digits=6&period=30`,
};
export const recoveryCodes = Array.from({ length: 10 }, (_, index) => `${(index + 1).toString(16).toUpperCase().padStart(6, "0")}-ABCDEF`);
