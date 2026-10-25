import { describe, expect, it } from "vitest";
import type { ContentPage } from "../api/contentPages";
import { contentDraft, contentInput, contentIssues, contentParagraphs } from "./contentPageDraft";
const page: ContentPage = { id: "aaaaaaaa-1111-2222-3333-444444444444", slug: "terms", title: "Terms", body: "Saved", isPublished: true,
  seoTitle: "Exact title", seoDescription: "Saved description", seoNoIndex: true };

describe("content draft rules", () => {
  it("creates a draft with explicit empty metadata and preserves all saved metadata/status on text edits", () => {
    expect(contentInput({ title: " New ", slug: "", body: "\nLiteral <b>tag</b>\n" })).toEqual({ title: "New", slug: null, body: "\nLiteral <b>tag</b>\n", isPublished: false,
      seo: { title: null, description: null, noIndex: false } });
    expect(contentInput({ ...contentDraft(page), title: "Updated", body: "Updated text" }, page)).toEqual({ title: "Updated", slug: "terms", body: "Updated text", isPublished: true,
      seo: { title: page.seoTitle, description: page.seoDescription, noIndex: true } });
  });
  it.each(["", " ", "a".repeat(201)])("rejects an invalid title without shortening it: %s", title => {
    expect(contentIssues({ title, slug: "valid", body: "" }, []).map(issue => issue.field)).toEqual(["title"]);
  });
  it("accepts exact title/body limits and rejects overlong raw body text", () => {
    expect(contentIssues({ title: "a".repeat(200), slug: "a".repeat(120), body: "x".repeat(20000) }, [])).toEqual([]);
    expect(contentIssues({ title: "A", slug: "a", body: " " + "x".repeat(20000) }, [])).toEqual([{ field: "body", key: "bodyInvalid" }]);
  });
  it.each(["Bad", "two--hyphens", "-start", "end-", "contains space", "https://shop.test", "p/x", "x".repeat(121)])("rejects invalid explicit slugs: %s", slug => {
    expect(contentIssues({ title: "Title", slug, body: "" }, []).map(issue => issue.field)).toEqual(["slug"]);
  });
  it("supports generated Czech slugs but requires an explicit slug if generation is empty", () => {
    expect(contentIssues({ title: "Žluťoučký kůň", slug: "", body: "" }, [])).toEqual([]);
    expect(contentIssues({ title: "中文", slug: "", body: "" }, [])).toEqual([{ field: "slug", key: "slugInvalid" }]);
    expect(contentIssues({ title: "中文", slug: "about", body: "" }, [])).toEqual([]);
    expect(contentIssues({ ...contentDraft(page), slug: "" }, [page], page)).toEqual([{ field: "slug", key: "slugInvalid" }]);
  });
  it("checks duplicate generated and explicit slugs against the current store, excluding the same page", () => {
    expect(contentIssues({ title: "Terms", slug: "", body: "" }, [page])).toEqual([{ field: "slug", key: "slugTaken" }]);
    expect(contentIssues(contentDraft(page), [page], page)).toEqual([]);
  });
  it("matches the storefront's plain paragraph format without interpreting markup", () => {
    expect(contentParagraphs("  <script>literal</script>\r\n\r\n**Words**\nwrapped\n \nEnd  ")).toEqual(["<script>literal</script>", "**Words**\nwrapped", "End"]);
  });
});
