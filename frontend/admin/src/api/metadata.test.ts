import { afterEach, describe, expect, it, vi } from "vitest";
import { metadataCategory, metadataFixture, metadataId, metadataListing, metadataPage, metadataStore } from "../test/metadataFixtures";
import { metadataApi, type MetadataValues } from "./metadata";

const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const empty: MetadataValues = { title: null, description: null, socialImageUrl: null, noIndex: false, pageText: null };
afterEach(() => vi.unstubAllGlobals());
describe("metadata contracts", () => {
  it.each(["store", "listing", "category", "page"] as const)("reads %s from its existing contract with fresh store defaults", async kind => {
    const source = kind === "listing" ? { ...metadataListing, categoryIds: [...metadataListing.categoryIds].reverse() } : kind === "category" ? [metadataCategory] : [metadataPage];
    const fetch = vi.fn().mockResolvedValueOnce(reply([metadataStore])).mockResolvedValueOnce(reply(source)); vi.stubGlobal("fetch", fetch);
    const signal = new AbortController().signal;
    expect(await metadataApi.read(metadataStore.id, kind, metadataId.toUpperCase(), signal)).toEqual(metadataFixture(kind));
    const paths = fetch.mock.calls.map(([url]) => url);
    expect(paths[0]).toBe("/api/admin/stores");
    if (kind === "store") expect(paths).toHaveLength(1);
    else expect(paths[1]).toBe("/api/admin/stores/" + metadataStore.id + "/" + (kind === "listing" ? "products/" + metadataId.toUpperCase() : kind === "category" ? "categories" : "pages"));
    expect(fetch.mock.calls.every(([, init]) => init.signal === signal && init.method === "GET")).toBe(true);
  });
  it.each(["store", "listing", "category", "page"] as const)("clears %s metadata explicitly and preserves every unrelated setting", async kind => {
    const response = kind === "store" ? { ...metadataStore, seo: { titleSuffix: null, description: null, socialImageUrl: null, noIndex: false } }
      : kind === "listing" ? { ...metadataListing, seoTitle: null, seoDescription: null, seoSocialImageUrl: null, seoNoIndex: false }
      : kind === "category" ? { ...metadataCategory, seoTitle: null, seoDescription: null, pageText: null }
      : { ...metadataPage, seoTitle: null, seoDescription: null, seoNoIndex: false };
    const fetch = vi.fn().mockResolvedValue(reply(response)); vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
    await metadataApi.save(metadataFixture(kind), empty, signal);
    const [url, init] = fetch.mock.calls[0], body = JSON.parse(init.body);
    expect(url).toBe("/api/admin/stores/" + metadataStore.id + (kind === "store" ? "" : "/" + (kind === "listing" ? "products" : kind === "category" ? "categories" : "pages") + "/" + metadataId));
    expect(init).toMatchObject({ method: "PUT", signal });
    if (kind === "store") expect(body).toEqual({ name: metadataStore.name, currency: "CZK", culture: "cs-CZ", theme: metadataStore.theme,
      company: metadataStore.company, returnWindowDays: 30, seo: { titleSuffix: null, description: null, socialImageUrl: null, noIndex: false } });
    if (kind === "listing") expect(body).toEqual({ name: metadataListing.name, slug: metadataListing.slug, description: metadataListing.description, price: 2499.5, vatRate: 21,
      isVisible: true, sortOrder: 3, taxClass: "standard", seo: { title: null, description: null, socialImageUrl: null, noIndex: false } });
    if (kind === "category") expect(body).toEqual({ name: metadataCategory.name, slug: "zidle", sortOrder: 7, parentId: metadataCategory.parentId, seo: { title: null, description: null, pageText: null } });
    if (kind === "page") expect(body).toEqual({ slug: "terms", title: "Podmínky", body: metadataPage.body, isPublished: true, seo: { title: null, description: null, noIndex: false } });
    expect(fetch).toHaveBeenCalledOnce();
  });
  it.each([{}, [{ ...metadataStore, seo: undefined }], [{ ...metadataStore, company: { legalName: "Incomplete" } }], [metadataStore, metadataStore], [{ ...metadataStore, seo: { ...metadataStore.seo, noIndex: null } }]])("rejects incomplete or ambiguous store facts: %j", async raw => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(raw)));
    await expect(metadataApi.read(metadataStore.id, "store", metadataStore.id)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each(["listing", "category", "page"] as const)("rejects missing %s metadata instead of supplying clearing defaults", async kind => {
    const item = kind === "listing" ? metadataListing : kind === "category" ? metadataCategory : metadataPage;
    const malformed = { ...item, seoDescription: undefined };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(reply([metadataStore])).mockResolvedValueOnce(reply(kind === "listing" ? malformed : [malformed])));
    await expect(metadataApi.read(metadataStore.id, kind, metadataId)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each(["category", "page"] as const)("distinguishes a missing %s from an empty draft", async kind => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(reply([metadataStore])).mockResolvedValueOnce(reply([])));
    await expect(metadataApi.read(metadataStore.id, kind, metadataId)).rejects.toMatchObject({ status: 404 });
  });
  it.each(["listing", "category"] as const)("rejects mismatched IDs after a %s save", async kind => {
    const item = kind === "listing" ? metadataListing : metadataCategory;
    const fetch = vi.fn().mockResolvedValue(reply({ ...item, id: metadataStore.id, seoTitle: null, seoDescription: null, seoSocialImageUrl: null, seoNoIndex: false, pageText: null })); vi.stubGlobal("fetch", fetch);
    await expect(metadataApi.save(metadataFixture(kind), empty)).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("does not call a reply successful if unrelated store settings changed or metadata was not applied", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply({ ...metadataStore, name: "Changed", seo: { titleSuffix: null, description: null, socialImageUrl: null, noIndex: false } })).mockResolvedValueOnce(reply(metadataStore));
    vi.stubGlobal("fetch", fetch);
    await expect(metadataApi.save(metadataFixture("store"), empty)).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(metadataApi.save(metadataFixture("store"), empty)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("blocks retired listing writes and aborts late reads without starting another request", async () => {
    const snapshot = metadataFixture("listing"); if (snapshot.kind !== "listing") throw new Error(); snapshot.item.archivedAt = "2026-10-25T10:00:00Z";
    const fetch = vi.fn().mockResolvedValue(reply([metadataStore])); vi.stubGlobal("fetch", fetch);
    await expect(metadataApi.save(snapshot, empty)).rejects.toBeInstanceOf(RangeError); expect(fetch).not.toHaveBeenCalled();
    const controller = new AbortController(); controller.abort();
    await expect(metadataApi.read(metadataStore.id, "listing", metadataId, controller.signal)).rejects.toMatchObject({ name: "AbortError" }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("never retries a failed save", async () => {
    const fetch = vi.fn().mockResolvedValue(reply({ title: "Conflict" }, 409)); vi.stubGlobal("fetch", fetch);
    await expect(metadataApi.save(metadataFixture("store"), empty)).rejects.toMatchObject({ status: 409 }); expect(fetch).toHaveBeenCalledOnce();
  });
});
