import type { AdminStore, Category, StoreProduct } from "../api";
import { contentPagesApi, type ContentPage } from "./contentPages";
import { HttpError, requestJson } from "./http";
import { isListing } from "./listings";

export type MetadataKind = "store" | "listing" | "category" | "page";
export type SearchDefaults = { titleSuffix: string | null; description: string | null; socialImageUrl: string | null; noIndex: boolean };
export type MetadataStore = AdminStore & { seo: SearchDefaults };
export type MetadataSnapshot = { store: MetadataStore } & (
  { kind: "store" } | { kind: "listing"; item: StoreProduct } | { kind: "category"; item: Category } | { kind: "page"; item: ContentPage }
);
export type MetadataValues = { title: string | null; description: string | null; socialImageUrl: string | null; noIndex: boolean; pageText: string | null };
const invalid = () => new HttpError("invalid-response", 200);
const record = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string";
const nullable = (value: unknown): value is string | null => value === null || text(value);
const id = (value: unknown): value is string => text(value) && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
const facts = (value: unknown) => JSON.stringify(value, (_key, item: unknown) => record(item) ? Object.fromEntries(Object.entries(item).sort(([left], [right]) => left.localeCompare(right))) : item);
function companyFields(value: unknown): value is AdminStore["company"] {
  return value === null || record(value) && ["legalName", "line1", "city", "postalCode", "country", "registrationNumber"].every(field => text(value[field])) && nullable(value.vatNumber);
}
function checkedStore(value: unknown): MetadataStore {
  if (!record(value) || !id(value.id) || !text(value.name) || !value.name.trim() || !text(value.currency) || !text(value.culture)
    || !["draft", "published"].includes(String(value.status)) || !nullable(value.logoUrl) || !nullable(value.primaryHostName)
    || !Number.isInteger(value.returnWindowDays) || !record(value.theme) || !text(value.theme.primaryColor) || !text(value.theme.secondaryColor) || !Number.isFinite(value.theme.borderRadius)
    || !record(value.seo) || !nullable(value.seo.titleSuffix) || !nullable(value.seo.description) || !nullable(value.seo.socialImageUrl) || typeof value.seo.noIndex !== "boolean"
    || !companyFields(value.company)) throw invalid();
  const company = value.company as AdminStore["company"];
  return { id: value.id, name: value.name, currency: value.currency, culture: value.culture, status: value.status as AdminStore["status"],
    logoUrl: value.logoUrl, primaryHostName: value.primaryHostName, returnWindowDays: value.returnWindowDays as number,
    theme: { primaryColor: value.theme.primaryColor, secondaryColor: value.theme.secondaryColor, borderRadius: value.theme.borderRadius as number },
    company: company === null ? null : { legalName: company.legalName, line1: company.line1, city: company.city, postalCode: company.postalCode,
      country: company.country, registrationNumber: company.registrationNumber, vatNumber: company.vatNumber },
    seo: { titleSuffix: value.seo.titleSuffix, description: value.seo.description, socialImageUrl: value.seo.socialImageUrl, noIndex: value.seo.noIndex } };
}
function checkedCategory(value: unknown): Category {
  if (!record(value) || !id(value.id) || !text(value.name) || !text(value.slug) || !Number.isInteger(value.sortOrder)
    || !(value.parentId === null || id(value.parentId)) || !Array.isArray(value.attributeIds) || !value.attributeIds.every(id)
    || !nullable(value.seoTitle) || !nullable(value.seoDescription) || !nullable(value.pageText)) throw invalid();
  return { id: value.id, name: value.name, slug: value.slug, sortOrder: value.sortOrder as number, parentId: value.parentId,
    attributeIds: [...value.attributeIds], seoTitle: value.seoTitle, seoDescription: value.seoDescription, pageText: value.pageText };
}
function checkedListing(value: unknown): StoreProduct {
  if (!isListing(value)) throw invalid();
  return { id: value.id, productId: value.productId, sku: value.sku, name: value.name, slug: value.slug, description: value.description,
    price: value.price, vatRate: value.vatRate, isVisible: value.isVisible, sortOrder: value.sortOrder, categoryIds: [...value.categoryIds].sort(), archivedAt: value.archivedAt,
    taxClass: value.taxClass, seoTitle: value.seoTitle, seoDescription: value.seoDescription, seoSocialImageUrl: value.seoSocialImageUrl, seoNoIndex: value.seoNoIndex };
}
function match<T extends { id: string }>(items: T[], wanted: string) {
  if (new Set(items.map(item => item.id.toLowerCase())).size !== items.length) throw invalid();
  const found = items.find(item => item.id.toLowerCase() === wanted.toLowerCase());
  if (!found) throw new HttpError("http", 404);
  return found;
}
async function storeRead(storeId: string, signal?: AbortSignal) {
  const raw = await requestJson<unknown>("/api/admin/stores", { signal }); signal?.throwIfAborted();
  if (!Array.isArray(raw)) throw invalid();
  return match(raw.map(checkedStore), storeId);
}
export function metadataName(snapshot: MetadataSnapshot) { return snapshot.kind === "store" ? snapshot.store.name : snapshot.kind === "page" ? snapshot.item.title : snapshot.item.name; }
export function metadataValues(snapshot: MetadataSnapshot): MetadataValues {
  if (snapshot.kind === "store") return { title: snapshot.store.seo.titleSuffix, description: snapshot.store.seo.description, socialImageUrl: snapshot.store.seo.socialImageUrl, noIndex: snapshot.store.seo.noIndex, pageText: null };
  return { title: snapshot.item.seoTitle, description: snapshot.item.seoDescription,
    socialImageUrl: snapshot.kind === "listing" ? snapshot.item.seoSocialImageUrl : null,
    noIndex: snapshot.kind === "listing" || snapshot.kind === "page" ? snapshot.item.seoNoIndex : false,
    pageText: snapshot.kind === "category" ? snapshot.item.pageText : null };
}
function unchanged(snapshot: MetadataSnapshot) {
  if (snapshot.kind === "store") { const { seo: _seo, ...rest } = snapshot.store; return rest; }
  const { seoTitle: _title, seoDescription: _description, ...rest } = snapshot.item;
  if (snapshot.kind === "listing") { const { seoSocialImageUrl: _image, seoNoIndex: _noIndex, ...fields } = rest as Omit<StoreProduct, "seoTitle" | "seoDescription">; return fields; }
  if (snapshot.kind === "page") { const { seoNoIndex: _noIndex, ...fields } = rest as Omit<ContentPage, "seoTitle" | "seoDescription">; return fields; }
  const { pageText: _pageText, ...fields } = rest as Omit<Category, "seoTitle" | "seoDescription">; return fields;
}
export const metadataApi = {
  read: async (storeId: string, kind: MetadataKind, resourceId: string, signal?: AbortSignal): Promise<MetadataSnapshot> => {
    const store = await storeRead(storeId, signal);
    if (kind === "store") return { kind, store };
    const root = `/api/admin/stores/${encodeURIComponent(storeId)}`;
    if (kind === "page") return { kind, store, item: match(await contentPagesApi.list(storeId, signal), resourceId) };
    if (kind === "category") {
      const raw = await requestJson<unknown>(`${root}/categories`, { signal }); signal?.throwIfAborted();
      if (!Array.isArray(raw)) throw invalid();
      return { kind, store, item: match(raw.map(checkedCategory), resourceId) };
    }
    const raw = await requestJson<unknown>(`${root}/products/${encodeURIComponent(resourceId)}`, { signal }); signal?.throwIfAborted();
    const item = checkedListing(raw); if (item.id.toLowerCase() !== resourceId.toLowerCase()) throw invalid();
    return { kind, store, item };
  },
  save: async (snapshot: MetadataSnapshot, values: MetadataValues, signal?: AbortSignal): Promise<MetadataSnapshot> => {
    const store = snapshot.store, root = `/api/admin/stores/${encodeURIComponent(store.id)}`;
    let result: MetadataSnapshot;
    if (snapshot.kind === "store") {
      const raw = await requestJson<unknown>(root, { method: "PUT", signal, body: { name: store.name, currency: store.currency, culture: store.culture,
        theme: store.theme, returnWindowDays: store.returnWindowDays, company: store.company,
        seo: { titleSuffix: values.title, description: values.description, socialImageUrl: values.socialImageUrl, noIndex: values.noIndex } } });
      result = { kind: "store", store: checkedStore(raw) };
    } else if (snapshot.kind === "page") {
      const page = snapshot.item;
      const item = await contentPagesApi.update(store.id, page.id, { slug: page.slug, title: page.title, body: page.body, isPublished: page.isPublished,
        seo: { title: values.title, description: values.description, noIndex: values.noIndex } }, signal);
      result = { kind: "page", store, item };
    } else if (snapshot.kind === "category") {
      const page = snapshot.item;
      const raw = await requestJson<unknown>(`${root}/categories/${encodeURIComponent(page.id)}`, { method: "PUT", signal,
        body: { name: page.name, slug: page.slug, parentId: page.parentId, sortOrder: page.sortOrder, seo: { title: values.title, description: values.description, pageText: values.pageText } } });
      result = { kind: "category", store, item: checkedCategory(raw) };
    } else {
      const page = snapshot.item;
      if (page.archivedAt !== null) throw new RangeError("A retired listing cannot be edited here.");
      const raw = await requestJson<unknown>(`${root}/products/${encodeURIComponent(page.id)}`, { method: "PUT", signal,
        body: { name: page.name, slug: page.slug, description: page.description, price: page.price, vatRate: page.vatRate, isVisible: page.isVisible, sortOrder: page.sortOrder, taxClass: page.taxClass,
          seo: { title: values.title, description: values.description, socialImageUrl: values.socialImageUrl, noIndex: values.noIndex } } });
      result = { kind: "listing", store, item: checkedListing(raw) };
    }
    signal?.throwIfAborted();
    if (facts(unchanged(snapshot)) !== facts(unchanged(result)) || facts(metadataValues(result)) !== facts(values)) throw invalid();
    return result;
  },
};
