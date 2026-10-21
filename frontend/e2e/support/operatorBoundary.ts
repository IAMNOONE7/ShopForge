import type { IsolatedRun } from "./isolatedEnvironment.ts";

export type OperatorScope = { adminOrigin: string; tenantId: string; ownerEmail: string; customerOrigin: string; customerEmail: string };
export function operatorScope(run: IsolatedRun, adminOrigin: string, customerOrigin: string): OperatorScope {
  return { adminOrigin, tenantId: run.tenantId, ownerEmail: `owner@${run.emailDomain}`, customerOrigin, customerEmail: `buyer@${run.emailDomain}` };
}
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === "object" && !Array.isArray(value);
const exact = (body: Record<string, unknown>, keys: string[]) => Object.keys(body).every(key => keys.includes(key)) && keys.every(key => key in body);
export function allowedOperatorWrite(scope: OperatorScope, method: string, url: URL, body: unknown) {
  if (method !== "POST" || url.username || url.password || url.search || url.hash || /%|\/\//.test(url.pathname)) return false;
  if (url.origin === scope.adminOrigin) {
    if (["/api/platform/auth/logout", "/api/admin/auth/logout", `/api/platform/tenants/${scope.tenantId}/suspend`, `/api/platform/tenants/${scope.tenantId}/resume`].includes(url.pathname)) return body === null;
    if (!object(body) || !exact(body, ["email", "password"]) || typeof body.password !== "string" || !body.password) return false;
    if (url.pathname === "/api/platform/auth/login") return body.email === "operator@e2e.test";
    return url.pathname === "/api/admin/auth/login" && body.email === scope.ownerEmail;
  }
  if (url.origin !== scope.customerOrigin || !object(body)) return false;
  if (url.pathname === "/api/storefront/account/verify") return exact(body, ["token"]) && typeof body.token === "string" && body.token.length > 0;
  if (url.pathname !== "/api/storefront/account/register") return false;
  return Object.keys(body).every(key => ["firstName", "lastName", "email", "password", "phone"].includes(key)) && body.email === scope.customerEmail
    && ["firstName", "lastName", "password"].every(key => typeof body[key] === "string" && body[key].length > 0);
}
