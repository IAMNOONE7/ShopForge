const accountPosts = new Set(["register", "verify", "login", "logout"].map(action => `/api/storefront/account/${action}`));

export function allowedBrowserWrite(method: string, url: URL, stores: ReadonlySet<string>, rigOrigin: string) {
  if (stores.has(url.origin)) {
    if (method === "POST") return url.pathname === "/api/storefront/cart/items" || url.pathname === "/api/storefront/checkout" || accountPosts.has(url.pathname) || /^\/api\/storefront\/account\/orders\/[^/]+\/returns$/.test(url.pathname);
    return method === "DELETE" && /^\/api\/storefront\/cart\/items\/[^/]+$/.test(url.pathname);
  }
  return method === "POST" && url.origin === rigOrigin && /^\/comgate\/pay\/[^/]+$/.test(url.pathname);
}
