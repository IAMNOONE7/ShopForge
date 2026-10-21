export function operatorPrefix(baseUrl = import.meta.env.BASE_URL) { return `/${baseUrl.split("/").filter(Boolean).concat("platform").join("/")}`; }
export function isOperatorPath(pathname: string, baseUrl = import.meta.env.BASE_URL) {
  const prefix = operatorPrefix(baseUrl); return pathname === prefix || pathname.startsWith(`${prefix}/`);
}
export function operatorHomePath(baseUrl = import.meta.env.BASE_URL) { return `${operatorPrefix(baseUrl)}/tenants`; }
