import { afterEach, expect, it, vi } from "vitest";
import { feedsApi } from "./feeds";
import { HttpError } from "./http";
import { feedCategories, googleFeed, heurekaFeed, zboziFeed } from "../test/feedFixtures";
const response = (value: unknown) => new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it("discovers the actual formats and preserves zero, null and previous output beside a later error", async () => {
  const fetch = vi.fn().mockResolvedValue(response([googleFeed, heurekaFeed, zboziFeed])); vi.stubGlobal("fetch", fetch);
  expect(await feedsApi.list("store/a")).toEqual([googleFeed, heurekaFeed, zboziFeed]);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%2Fa/feeds");
});
it.each([
  null, {}, [googleFeed, googleFeed], [{ ...googleFeed, feed: "../google" }], [{ ...googleFeed, isEnabled: "true" }],
  [{ ...googleFeed, lastProducts: -1 }], [{ ...googleFeed, lastBytes: 9007199254740992 }], [{ ...googleFeed, lastSkipped: 0.2 }],
  [{ ...googleFeed, lastRunAt: "yesterday" }], [{ ...googleFeed, lastError: {} }], [{ ...googleFeed, lastMilliseconds: undefined }],
])("rejects malformed feed data instead of manufacturing settings or empty results: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(value))); await expect(feedsApi.list("store")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([
  "javascript:alert(1)", "http://shop.example.test/api/storefront/feeds/google.xml?token=fixture",
  "https://user:pass@shop.example.test/api/storefront/feeds/google.xml?token=fixture", "https://shop.example.test/admin/?token=fixture",
  "https://shop.example.test/api/storefront/feeds/heureka.xml?token=fixture", "https://shop.example.test/api/storefront/feeds/google.xml?token=",
  "https://shop.example.test/api/storefront/feeds/google.xml?token=a&token=b", "https://shop.example.test/api/storefront/feeds/google.xml?token=a#fragment",
])("does not offer an unsafe or misidentified collection address: %s", async url => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response([{ ...googleFeed, url }]))); await expect(feedsApi.list("store")).rejects.toBeInstanceOf(HttpError);
});
it("sends exact settings, bodyless production/rotation and encoded category writes with cancellation", async () => {
  const rotated = { ...googleFeed, url: googleFeed.url!.replace("fixture-google-access", "fixture-replaced-access") };
  const fetch = vi.fn().mockResolvedValueOnce(response({ ...googleFeed, isEnabled: false })).mockResolvedValueOnce(response(rotated))
    .mockResolvedValueOnce(response(googleFeed)).mockResolvedValueOnce(response({ ...feedCategories[0], engineCategory: "" })); vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal;
  await feedsApi.save("store", "google", false, signal); await feedsApi.rotate("store", googleFeed, signal); await feedsApi.run("store", "google", signal);
  expect(await feedsApi.mapCategory("store/a", "google", "category/a", null, signal).catch(error => error)).toMatchObject({ kind: "invalid-response" });
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body, options.signal])).toEqual([
    ["/api/admin/stores/store/feeds/google", "PUT", '{"isEnabled":false}', signal],
    ["/api/admin/stores/store/feeds/google/token", "POST", undefined, signal],
    ["/api/admin/stores/store/feeds/google/run", "POST", undefined, signal],
    ["/api/admin/stores/store%2Fa/feeds/google/categories/category%2Fa", "PUT", '{"engineCategory":null}', signal],
  ]);
});
it("checks mapping identity, trims the response and accepts the API's empty-string clearing response", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(response(feedCategories)).mockResolvedValueOnce(response({ ...feedCategories[0], engineCategory: "  Changed path  " }))
    .mockResolvedValueOnce(response({ ...feedCategories[0], engineCategory: "" })); vi.stubGlobal("fetch", fetch);
  expect(await feedsApi.categories("store", "google")).toEqual(feedCategories);
  expect((await feedsApi.mapCategory("store", "google", feedCategories[0].categoryId, "Changed path")).engineCategory).toBe("Changed path");
  expect((await feedsApi.mapCategory("store", "google", feedCategories[0].categoryId, null)).engineCategory).toBeNull();
});
it.each([
  ["save", { ...googleFeed, isEnabled: false }], ["save", { ...googleFeed, feed: "heureka", url: null }],
  ["rotate", googleFeed], ["run", { ...googleFeed, lastError: "Failed" }], ["run", { ...googleFeed, lastRunAt: null }], ["run", { ...googleFeed, lastProducts: null }],
] as const)("does not acknowledge a mismatched %s answer", async (method, value) => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(value)));
  await expect(method === "save" ? feedsApi.save("store", "google", true) : method === "rotate" ? feedsApi.rotate("store", googleFeed) : feedsApi.run("store", "google")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([{}, [{ ...feedCategories[0], engineCategory: 12 }], [feedCategories[0], feedCategories[0]]])("rejects malformed mapping collections: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(value))); await expect(feedsApi.categories("store", "google")).rejects.toMatchObject({ kind: "invalid-response" });
});
it("keeps every literal product problem and does not turn a failed check into a clean result", async () => {
  const problems = [{ sku: "SKU-1", name: "<Chair>", problems: ["No image <required>", "No brand"] }];
  const fetch = vi.fn().mockResolvedValueOnce(response(problems)).mockResolvedValueOnce(new Response(null, { status: 404 })); vi.stubGlobal("fetch", fetch);
  expect(await feedsApi.check("store", "google")).toEqual(problems); await expect(feedsApi.check("store", "google")).rejects.toMatchObject({ status: 404 });
});
it.each([{}, [{ sku: "A", name: "Chair", problems: [] }], [{ sku: "A", name: "Chair", problems: [42] }]])("rejects malformed checks: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(value))); await expect(feedsApi.check("store", "google")).rejects.toMatchObject({ kind: "invalid-response" });
});
it("retains a production refusal and its exact reason rather than fetching or retrying the public file", async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: "The feed could not be produced", detail: "No proved domain <required>" }), { status: 409, headers: { "Content-Type": "application/problem+json" } })); vi.stubGlobal("fetch", fetch);
  await expect(feedsApi.run("store", "google")).rejects.toMatchObject({ status: 409, problem: { detail: "No proved domain <required>" } }); expect(fetch).toHaveBeenCalledTimes(1);
});
