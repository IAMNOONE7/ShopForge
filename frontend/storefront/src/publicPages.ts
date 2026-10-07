// The shop's public page addresses, written down once. The backend names the same addresses in sitemaps,
// canonical tags, shopping feeds and the mail a shop sends, so their shape is a contract between the two
// rather than a detail of either (D-164). `StoreAddress` on the server builds them from the same segments, and
// a test on each side pins the result: changing one alone is how a sitemap starts advertising pages that do
// not exist.
const product = "p";
const category = "c";
const contentPage = "pages";

export const publicRoutes = {
  product: `${product}/:slug`,
  category: `${category}/:slug`,
  contentPage: `${contentPage}/:slug`,
} as const;

export const productPath = (slug: string) => `/${product}/${encodeURIComponent(slug)}`;

export const categoryPath = (slug: string) => `/${category}/${encodeURIComponent(slug)}`;

export const contentPagePath = (slug: string) => `/${contentPage}/${encodeURIComponent(slug)}`;
