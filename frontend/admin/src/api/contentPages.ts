import { generatedCode, validCode } from "../components/attributeValidation";
import { HttpError, requestJson } from "./http";

export type ContentPage = {
  id: string; slug: string; title: string; body: string; isPublished: boolean;
  seoTitle: string | null; seoDescription: string | null; seoNoIndex: boolean;
};
export type ContentPageInput = {
  slug: string | null; title: string; body: string; isPublished: boolean;
  seo: { title: string | null; description: string | null; noIndex: boolean };
};
export const isContentPageId = (value: string) => /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/pages`;
function checked(value: unknown, status = 200): ContentPage {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new HttpError("invalid-response", status);
  const row = value as Record<string, unknown>;
  if (typeof row.id !== "string" || !isContentPageId(row.id) || typeof row.slug !== "string" || !validCode(row.slug)
    || typeof row.title !== "string" || !row.title.trim() || typeof row.body !== "string" || typeof row.isPublished !== "boolean"
    || !(row.seoTitle === null || typeof row.seoTitle === "string") || !(row.seoDescription === null || typeof row.seoDescription === "string")
    || typeof row.seoNoIndex !== "boolean") throw new HttpError("invalid-response", status);
  return { id: row.id, slug: row.slug, title: row.title, body: row.body, isPublished: row.isPublished,
    seoTitle: row.seoTitle, seoDescription: row.seoDescription, seoNoIndex: row.seoNoIndex };
}
async function save(storeId: string, input: ContentPageInput, pageId?: string, signal?: AbortSignal) {
  const status = pageId ? 200 : 201;
  const raw = await requestJson<unknown>(`${root(storeId)}${pageId ? `/${encodeURIComponent(pageId)}` : ""}`, { method: pageId ? "PUT" : "POST", body: input, signal });
  signal?.throwIfAborted();
  const page = checked(raw, status);
  const expectedSlug = input.slug ?? (pageId ? null : generatedCode(input.title));
  if (pageId && page.id.toLowerCase() !== pageId.toLowerCase() || expectedSlug !== null && page.slug !== expectedSlug
    || page.title !== input.title.trim() || page.body !== input.body.trim() || page.isPublished !== input.isPublished
    || page.seoTitle !== (input.seo.title?.trim() || null) || page.seoDescription !== (input.seo.description?.trim() || null)
    || page.seoNoIndex !== input.seo.noIndex) throw new HttpError("invalid-response", status);
  return page;
}
export const contentPagesApi = {
  list: async (storeId: string, signal?: AbortSignal): Promise<ContentPage[]> => {
    const value = await requestJson<unknown>(root(storeId), { signal }); signal?.throwIfAborted();
    if (!Array.isArray(value)) throw new HttpError("invalid-response", 200);
    const pages = value.map(item => checked(item));
    if (new Set(pages.map(page => page.id.toLowerCase())).size !== pages.length || new Set(pages.map(page => page.slug)).size !== pages.length) throw new HttpError("invalid-response", 200);
    return pages;
  },
  create: (storeId: string, input: ContentPageInput, signal?: AbortSignal) => save(storeId, input, undefined, signal),
  update: (storeId: string, pageId: string, input: ContentPageInput, signal?: AbortSignal) => save(storeId, input, pageId, signal),
  remove: async (storeId: string, pageId: string, signal?: AbortSignal) => {
    const reply = await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(pageId)}`, { method: "DELETE", signal }); signal?.throwIfAborted();
    if (reply !== undefined) throw new HttpError("invalid-response", 204);
  },
};
