import { isOperatorPath } from "./platform/routing";

export function adminHomePath(baseUrl = import.meta.env.BASE_URL) {
  return `${normalizedBase(baseUrl)}/products`;
}

export function safeAdminReturnPath(
  value: string,
  baseUrl = import.meta.env.BASE_URL,
) {
  const fallback = adminHomePath(baseUrl);
  if (!value.startsWith("/") || value.startsWith("//")) return fallback;
  if (isOperatorPath(value.split(/[?#]/)[0], baseUrl)) return fallback;
  const base = normalizedBase(baseUrl);
  if (base && value !== base && !value.startsWith(`${base}/`)) return fallback;
  return value;
}

function normalizedBase(baseUrl: string) {
  if (baseUrl === "/") return "";
  return `/${baseUrl.split("/").filter(Boolean).join("/")}`;
}
