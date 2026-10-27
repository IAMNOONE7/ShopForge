import { HttpError, requestJson } from "./http";

export type StoreFeed = {
  feed: string;
  isEnabled: boolean;
  url: string | null;
  lastRunAt: string | null;
  lastMilliseconds: number | null;
  lastBytes: number | null;
  lastProducts: number | null;
  lastSkipped: number | null;
  lastError: string | null;
};
export type FeedCategory = { categoryId: string; name: string; engineCategory: string | null };
export type FeedProblem = { sku: string; name: string; problems: string[] };
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const count = (value: unknown) => value === null || Number.isSafeInteger(value) && Number(value) >= 0;
const invalid = () => new HttpError("invalid-response", 200);
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/feeds`;
const path = (storeId: string, feed: string) => `${root(storeId)}/${encodeURIComponent(feed)}`;

function feedAddress(value: unknown, feed: string): value is string | null {
  if (value === null) return true;
  if (!text(value)) return false;
  try {
    const url = new URL(value);
    return url.protocol === "https:" && !url.username && !url.password && !url.hash &&
      url.pathname === `/api/storefront/feeds/${feed}.xml` && url.searchParams.size === 1 && text(url.searchParams.get("token"));
  } catch { return false; }
}
function checked(value: unknown, feed?: string): StoreFeed {
  if (!object(value) || !text(value.feed) || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value.feed) ||
    feed !== undefined && value.feed !== feed || typeof value.isEnabled !== "boolean" || !feedAddress(value.url, value.feed) ||
    !(value.lastRunAt === null || text(value.lastRunAt) && Number.isFinite(Date.parse(value.lastRunAt))) ||
    ![value.lastMilliseconds, value.lastBytes, value.lastProducts, value.lastSkipped].every(count) ||
    !(value.lastError === null || typeof value.lastError === "string")) throw invalid();
  return { feed: value.feed, isEnabled: value.isEnabled, url: value.url, lastRunAt: value.lastRunAt as string | null,
    lastMilliseconds: value.lastMilliseconds as number | null, lastBytes: value.lastBytes as number | null,
    lastProducts: value.lastProducts as number | null, lastSkipped: value.lastSkipped as number | null, lastError: value.lastError as string | null };
}
function category(value: unknown): FeedCategory {
  if (!object(value) || !text(value.categoryId) || !text(value.name) || !(value.engineCategory === null || typeof value.engineCategory === "string")) throw invalid();
  return { categoryId: value.categoryId, name: value.name, engineCategory: typeof value.engineCategory === "string" ? value.engineCategory.trim() || null : null };
}
export const feedsApi = {
  async list(storeId: string, signal?: AbortSignal): Promise<StoreFeed[]> {
    const value = await requestJson<unknown>(root(storeId), { signal });
    if (!Array.isArray(value)) throw invalid();
    const feeds = value.map(item => checked(item));
    if (new Set(feeds.map(item => item.feed)).size !== feeds.length) throw invalid();
    return feeds;
  },
  async save(storeId: string, feed: string, isEnabled: boolean, signal?: AbortSignal) {
    const value = checked(await requestJson<unknown>(path(storeId, feed), { method: "PUT", body: { isEnabled }, signal }), feed);
    if (value.isEnabled !== isEnabled) throw invalid();
    return value;
  },
  async rotate(storeId: string, before: StoreFeed, signal?: AbortSignal) {
    const value = checked(await requestJson<unknown>(path(storeId, before.feed) + "/token", { method: "POST", signal }), before.feed);
    if (before.url !== null && value.url !== null && new URL(before.url).searchParams.get("token") === new URL(value.url).searchParams.get("token")) throw invalid();
    return value;
  },
  async run(storeId: string, feed: string, signal?: AbortSignal) {
    const value = checked(await requestJson<unknown>(path(storeId, feed) + "/run", { method: "POST", signal }), feed);
    if (!value.isEnabled || value.lastError !== null || value.lastRunAt === null ||
      [value.lastMilliseconds, value.lastBytes, value.lastProducts, value.lastSkipped].some(item => item === null)) throw invalid();
    return value;
  },
  async categories(storeId: string, feed: string, signal?: AbortSignal) {
    const value = await requestJson<unknown>(path(storeId, feed) + "/categories", { signal });
    if (!Array.isArray(value)) throw invalid();
    const rows = value.map(category);
    if (new Set(rows.map(row => row.categoryId)).size !== rows.length) throw invalid();
    return rows;
  },
  async mapCategory(storeId: string, feed: string, categoryId: string, engineCategory: string | null, signal?: AbortSignal) {
    const value = category(await requestJson<unknown>(`${path(storeId, feed)}/categories/${encodeURIComponent(categoryId)}`, { method: "PUT", body: { engineCategory }, signal }));
    if (value.categoryId !== categoryId || value.engineCategory !== (engineCategory?.trim() || null)) throw invalid();
    return value;
  },
  async check(storeId: string, feed: string, signal?: AbortSignal): Promise<FeedProblem[]> {
    const value = await requestJson<unknown>(path(storeId, feed) + "/check", { signal });
    if (!Array.isArray(value) || !value.every(row => object(row) && text(row.sku) && typeof row.name === "string" && Array.isArray(row.problems) && row.problems.length > 0 && row.problems.every(text))) throw invalid();
    return value.map(row => ({ sku: row.sku, name: row.name, problems: [...row.problems] }));
  },
};
