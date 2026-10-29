import { afterEach, expect, it, vi } from "vitest";
import { getContentPage } from "./api";

const page = { slug: "kontakt", title: "Kontakt", body: "Údaje obchodu\n<script>literal</script>",
  seo: { title: "Kontakt", description: null, socialImageUrl: null, noIndex: false, canonical: null } };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());

it("reads only the requested published page and preserves its literal content and metadata", async () => {
  const fetch = vi.fn().mockResolvedValue(json(page));
  vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal;
  expect(await getContentPage("kontakt", signal)).toEqual(page);
  expect(fetch).toHaveBeenCalledExactlyOnceWith("/api/storefront/pages/kontakt", expect.objectContaining({ method: "GET", signal }));
});

it.each([null, [], {}, { ...page, slug: "another-store-page" }, { ...page, title: " " },
  { ...page, title: 42 }, { ...page, body: null }, { ...page, body: {} }])("rejects a malformed or mismatched page: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value)));
  await expect(getContentPage("kontakt", new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
});

it("keeps a published empty body distinct from a failed read", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ ...page, body: "" })));
  expect((await getContentPage("kontakt", new AbortController().signal)).body).toBe("");
});

it("preserves a missing publication without probing aliases or creating content", async () => {
  const fetch = vi.fn().mockResolvedValue(json({}, 404));
  vi.stubGlobal("fetch", fetch);
  await expect(getContentPage("o-obchodu", new AbortController().signal)).rejects.toMatchObject({ status: 404 });
  expect(fetch).toHaveBeenCalledTimes(1);
});
