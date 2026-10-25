import { afterEach, describe, expect, it, vi } from "vitest";
import { contentPagesApi, type ContentPage, type ContentPageInput } from "./contentPages";

const id = "aaaaaaaa-1111-2222-3333-444444444444";
const page: ContentPage = { id, slug: "terms", title: "Terms", body: "First paragraph.\n\n<script>literal</script>", isPublished: true,
  seoTitle: "Exact search title", seoDescription: "Exact saved description", seoNoIndex: true };
const input: ContentPageInput = { slug: page.slug, title: page.title, body: page.body, isPublished: page.isPublished,
  seo: { title: page.seoTitle, description: page.seoDescription, noIndex: page.seoNoIndex } };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());

describe("content-page contracts", () => {
  it("uses the scoped list, POST, PUT and DELETE with checked replies and no detail GET", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply([{ ...page, extra: "private" }])).mockResolvedValueOnce(reply(page, 201))
      .mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
    expect(await contentPagesApi.list("store/a", signal)).toEqual([page]);
    expect(await contentPagesApi.create("store/a", input, signal)).toEqual(page);
    expect(await contentPagesApi.update("store/a", id, input, signal)).toEqual(page);
    await contentPagesApi.remove("store/a", id, signal);
    expect(fetch.mock.calls.map(([url, init]) => [url, init.method, init.body, init.signal])).toEqual([
      ["/api/admin/stores/store%2Fa/pages", "GET", undefined, signal],
      ["/api/admin/stores/store%2Fa/pages", "POST", JSON.stringify(input), signal],
      [`/api/admin/stores/store%2Fa/pages/${id}`, "PUT", JSON.stringify(input), signal],
      [`/api/admin/stores/store%2Fa/pages/${id}`, "DELETE", undefined, signal],
    ]);
  });
  it.each([{}, null, [page, page], [page, { ...page, id: "bbbbbbbb-1111-2222-3333-444444444444" }],
    [{ ...page, id: "wrong" }], [{ ...page, slug: "Bad Address" }], [{ ...page, title: "" }], [{ ...page, body: null }],
    [{ ...page, isPublished: "false" }], [{ ...page, seoTitle: undefined }], [{ ...page, seoDescription: 1 }], [{ ...page, seoNoIndex: null }],
  ])("rejects malformed or duplicate list facts: %j", async value => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(value)));
    await expect(contentPagesApi.list("store")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each([{ id: "bbbbbbbb-1111-2222-3333-444444444444" }, { slug: "another" }, { title: "Another" }, { body: "Changed" },
    { isPublished: false }, { seoTitle: null }, { seoDescription: null }, { seoNoIndex: false }])("does not confirm an unapplied or mismatched update: %j", async change => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply({ ...page, ...change })));
    await expect(contentPagesApi.update("store", id, input)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("accepts server slug generation and trimming while preserving a null slug on update", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply({ ...page, title: "Žluté židle", slug: "zlute-zidle", body: "Text" }, 201)).mockResolvedValueOnce(reply(page));
    vi.stubGlobal("fetch", fetch);
    await expect(contentPagesApi.create("store", { ...input, slug: null, title: " Žluté židle ", body: " Text " })).resolves.toMatchObject({ slug: "zlute-zidle" });
    await expect(contentPagesApi.update("store", id.toUpperCase(), { ...input, slug: null })).resolves.toEqual(page);
  });
  it("does not replay refusals or accept a late aborted reply", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply({ title: "The slug is already used in this store" }, 409)).mockResolvedValueOnce(reply([page]));
    vi.stubGlobal("fetch", fetch);
    await expect(contentPagesApi.create("store", input)).rejects.toMatchObject({ status: 409 });
    const controller = new AbortController(); controller.abort();
    await expect(contentPagesApi.list("store", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it("does not treat an unexpected delete response as confirmed", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply({})));
    await expect(contentPagesApi.remove("store", id)).rejects.toMatchObject({ kind: "invalid-response" });
  });
});
