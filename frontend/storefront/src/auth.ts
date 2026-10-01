export type AuthIssue = {
  field: "email" | "password" | "firstName" | "lastName";
  key: "emailInvalid" | "passwordInvalid" | "nameInvalid";
};

type Credentials = { email: string; password: string };
type RegistrationDraft = Credentials & {
  firstName: string;
  lastName: string;
};

type ProfileDraft = Pick<RegistrationDraft, "firstName" | "lastName">;

export function signInIssues(value: Credentials): AuthIssue[] {
  const issues: AuthIssue[] = [];
  if (!isEmail(value.email)) issues.push({ field: "email", key: "emailInvalid" });
  if (!value.password) issues.push({ field: "password", key: "passwordInvalid" });
  return issues;
}

export function registrationIssues(value: RegistrationDraft): AuthIssue[] {
  const issues = signInIssues(value).filter((issue) => issue.field === "email");
  if (!isName(value.firstName))
    issues.unshift({ field: "firstName", key: "nameInvalid" });
  if (!isName(value.lastName)) {
    const emailIndex = issues.findIndex((issue) => issue.field === "email");
    issues.splice(emailIndex < 0 ? issues.length : emailIndex, 0, {
      field: "lastName",
      key: "nameInvalid",
    });
  }
  if (!isPassword(value.password))
    issues.push({ field: "password", key: "passwordInvalid" });
  return issues;
}

export function profileIssues(value: ProfileDraft): AuthIssue[] {
  const issues: AuthIssue[] = [];
  if (!isName(value.firstName))
    issues.push({ field: "firstName", key: "nameInvalid" });
  if (!isName(value.lastName))
    issues.push({ field: "lastName", key: "nameInvalid" });
  return issues;
}

export function passwordIssues(password: string): AuthIssue[] {
  return isPassword(password)
    ? []
    : [{ field: "password", key: "passwordInvalid" }];
}

export function emailIssues(email: string): AuthIssue[] {
  return isEmail(email) ? [] : [{ field: "email", key: "emailInvalid" }];
}

export function safeAuthReturn(value: string | null, fallback = "/account") {
  if (!value?.startsWith("/") || value.startsWith("//")) return fallback;
  try {
    const url = new URL(value, "https://shopforge.invalid");
    if (url.origin !== "https://shopforge.invalid") return fallback;
    const allowed =
      url.pathname === "/checkout" ||
      url.pathname === "/account" ||
      url.pathname.startsWith("/account/orders/");
    if (!allowed) return fallback;
    return url.pathname + url.search + url.hash;
  } catch {
    return fallback;
  }
}

export function authPath(path: string, returnTo: string) {
  const safe = safeAuthReturn(returnTo);
  return `${path}?returnTo=${encodeURIComponent(safe)}`;
}

function isEmail(value: string) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
}

function isName(value: string) {
  const length = value.trim().length;
  return length > 0 && length <= 100;
}

function isPassword(value: string) {
  return value.length >= 10 && value.length <= 128;
}

const authReturnKey = "shopforge.auth.returnTo";

export function rememberAuthReturn(returnTo: string) {
  if (typeof window === "undefined") return;
  try {
    window.sessionStorage.setItem(authReturnKey, safeAuthReturn(returnTo));
  } catch {
    return;
  }
}

export function resolveAuthReturn(value: string | null) {
  if (value !== null) return safeAuthReturn(value);
  if (typeof window === "undefined") return "/account";
  try {
    return safeAuthReturn(window.sessionStorage.getItem(authReturnKey));
  } catch {
    return "/account";
  }
}

export function clearAuthReturn() {
  if (typeof window === "undefined") return;
  try {
    window.sessionStorage.removeItem(authReturnKey);
  } catch {
    return;
  }
}
