import { describe, expect, it } from "vitest";
import { metadataFixture, metadataStore } from "../test/metadataFixtures";
import { metadataDraft, metadataInput, metadataIssues, metadataPreview } from "./metadataDraft";

const blank = { title: " ", description: "\n", socialImageUrl: " ", noIndex: false, pageText: " " };
describe("metadata drafts and fallback previews", () => {
  it.each(["store", "listing", "category", "page"] as const)("clears blank %s values explicitly with false no-index", kind => {
    expect(metadataInput(blank, kind)).toEqual({ title: null, description: null, socialImageUrl: null, noIndex: false, pageText: null });
  });
  it("accepts the exact field limits and rejects oversized trimmed values without truncation", () => {
    const exact = { ...blank, title: "x".repeat(200), description: "y".repeat(500) };
    expect(metadataIssues(exact, "store")).toEqual([]);
    expect(metadataIssues({ ...exact, title: exact.title + "x", description: exact.description + "y" }, "store").map(issue => issue.field)).toEqual(["title", "description"]);
  });
  it.each(["javascript:alert(1)", "data:image/png;base64,AA", "/image.png", "//example.test/image.png", "https:", "https://", "https://example.test/" + "a".repeat(2000)])("rejects unsafe or incomplete sharing address %s", socialImageUrl => {
    expect(metadataIssues({ ...blank, socialImageUrl }, "listing")).toEqual([{ field: "socialImageUrl", key: "imageInvalid" }]);
  });
  it.each(["http://example.test/a.png", " HTTPS://example.test/a.png ", "https://example.test/" + "a".repeat(1979)])("accepts an optional absolute address %s", socialImageUrl => {
    expect(metadataIssues({ ...blank, socialImageUrl }, "store")).toEqual([]);
  });
  it("does not invent category/page image fields, category no-index or a category-text length limit", () => {
    const draft = { ...blank, noIndex: true, socialImageUrl: "ignored", pageText: "<b>literal</b>\n\n" + "x".repeat(20001) };
    expect(metadataIssues(draft, "category")).toEqual([]); expect(metadataInput(draft, "category")).toMatchObject({ socialImageUrl: null, noIndex: false, pageText: draft.pageText });
    expect(metadataInput(draft, "page")).toMatchObject({ socialImageUrl: null, pageText: null, noIndex: true });
  });
  it("uses an exact page title or a name plus suffix, with description/image fallback and store-dominant no-index", () => {
    const snapshot = metadataFixture("listing");
    expect(metadataPreview(snapshot, blank)).toEqual({ title: "Žlutá židle — Dílna", description: metadataStore.seo.description, image: metadataStore.seo.socialImageUrl, noIndex: true });
    expect(metadataPreview(snapshot, { ...blank, title: " Exact title ", description: " Own description ", socialImageUrl: "https://example.test/own.png" })).toEqual({ title: "Exact title", description: "Own description", image: "https://example.test/own.png", noIndex: true });
    snapshot.store.seo.noIndex = false; snapshot.store.seo.titleSuffix = null;
    expect(metadataPreview(snapshot, blank)).toMatchObject({ title: "Žlutá židle", noIndex: false });
  });
  it("previews clearing the actual store defaults rather than falling back to their old values", () => {
    const snapshot = metadataFixture("store");
    expect(metadataDraft(snapshot)).toMatchObject({ title: "Dílna", noIndex: true });
    expect(metadataPreview(snapshot, blank)).toEqual({ title: metadataStore.name, description: null, image: null, noIndex: false });
  });
});
