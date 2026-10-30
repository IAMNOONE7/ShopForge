import { strict as assert } from "node:assert";
import { createHash } from "node:crypto";
import { EventEmitter } from "node:events";
import { test } from "node:test";
import type { Page, Request, Response } from "@playwright/test";
import { observeHostedAssets } from "./hostedAssets.ts";

const expected = Buffer.from("verified package asset");
const headers = { "cache-control": "public, max-age=31536000, immutable", "content-type": "text/javascript" };

function capture(options: { path?: string; status?: number; headers?: Record<string, string>; body?: () => Promise<Buffer>; buildFile?: () => Buffer } = {}) {
  const page = new EventEmitter(), frame = {}, state = { closed: false, failure: null as { errorText: string } | null };
  const mockPage = Object.assign(page, { mainFrame: () => frame, isClosed: () => state.closed }) as unknown as Page;
  const path = options.path ?? "/assets/CartPage-test.js", files: string[] = [];
  const request = { url: () => `http://store.localhost${path}`, failure: () => state.failure } as Request;
  const response = { url: request.url, request: () => request, status: () => options.status ?? 200,
    headers: () => options.headers ?? headers, body: options.body ?? (async () => expected) } as Response;
  const facts = { assets: [] as { path: string; sha256: string }[], failures: [] as string[], checks: [] as Promise<void>[] };
  observeHostedAssets(mockPage, facts, file => { files.push(file); return options.buildFile ? options.buildFile() : expected; });
  page.emit("request", request);
  return { page, frame, state, path, request, response, facts, files, respond: () => page.emit("response", response), finish: () => Promise.all(facts.checks) };
}

for (const path of ["/assets/CartPage-test.js", "/assets/index-test.css", "/admin/assets/index-test.js", "/admin/assets/index-test.css"]) {
  test(`checks complete package bytes and headers for ${path}`, async () => {
    const h = capture({ path, headers: { ...headers, "content-type": path.endsWith(".css") ? "text/css" : "text/javascript" } });
    h.respond(); await h.finish();
    assert.deepEqual(h.facts.failures, []); assert.deepEqual(h.files, [path]);
    assert.deepEqual(h.facts.assets, [{ path, sha256: createHash("sha256").update(expected).digest("hex") }]);
  });
}

for (const path of ["/assets/CartPage-test.js", "/admin/assets/index-test.css"]) {
  test(`ignores a confirmed aborted request and unread body for ${path}`, async () => {
    const h = capture({ path, headers: { ...headers, "content-type": path.endsWith(".css") ? "text/css" : "text/javascript" },
      body: async () => { throw new Error("Response body unavailable"); } });
    h.state.failure = { errorText: "net::ERR_ABORTED" }; h.page.emit("requestfailed", h.request); h.respond(); await h.finish();
    assert.deepEqual(h.facts.failures, []); assert.deepEqual(h.facts.assets, []); assert.deepEqual(h.files, []);
  });
}

for (const errorText of ["net::ERR_FAILED", "net::ERR_CONNECTION_RESET", "net::ERR_NAME_NOT_RESOLVED"]) {
  test(`still reports a real asset request failure: ${errorText}`, () => {
    const h = capture(); h.state.failure = { errorText }; h.page.emit("requestfailed", h.request);
    assert.deepEqual(h.facts.failures, [h.path]);
  });
}

for (const beforeResponse of [false, true]) {
  test(`ignores an unavailable old-document body when navigation ${beforeResponse ? "precedes" : "follows"} its response`, async () => {
    let reject!: (error: Error) => void;
    const body = new Promise<Buffer>((_resolve, no) => { reject = no; });
    const h = capture({ body: () => body });
    if (beforeResponse) h.page.emit("framenavigated", h.frame);
    h.respond();
    if (!beforeResponse) h.page.emit("framenavigated", h.frame);
    reject(new Error("No resource with given identifier found")); await h.finish();
    assert.deepEqual(h.facts.failures, []); assert.deepEqual(h.facts.assets, []);
  });
}

test("ignores an unavailable response body after page teardown", async () => {
  const h = capture({ body: async () => { throw new Error("Page closed"); } });
  h.respond(); h.state.closed = true; await h.finish();
  assert.deepEqual(h.facts.failures, []); assert.deepEqual(h.facts.assets, []);
});

test("reports an unavailable body on the current document without guessing from its error text", async () => {
  const h = capture({ body: async () => { throw new Error("net::ERR_ABORTED or page moved on"); } });
  h.respond(); await h.finish(); assert.deepEqual(h.facts.failures, [h.path]);
});

test("an unrelated frame navigation does not hide a current-document body failure", async () => {
  const h = capture({ body: async () => { throw new Error("Body unavailable"); } });
  h.respond(); h.page.emit("framenavigated", {}); await h.finish(); assert.deepEqual(h.facts.failures, [h.path]);
});

for (const options of [{ status: 404 }, { status: 503 }, { headers: { ...headers, "cache-control": "no-cache" } },
  { headers: { ...headers, "content-type": "text/html" } }]) {
  test(`navigation never hides invalid asset status or headers: ${JSON.stringify(options)}`, async () => {
    const h = capture({ ...options, body: async () => { throw new Error("Body unavailable"); } });
    h.page.emit("framenavigated", h.frame); h.respond(); await h.finish();
    assert.deepEqual(h.facts.failures, [h.path]); assert.deepEqual(h.facts.assets, []);
  });
}

test("navigation never hides a complete asset whose bytes differ from the package", async () => {
  const h = capture({ body: async () => Buffer.from("different bytes") });
  h.respond(); h.page.emit("framenavigated", h.frame); await h.finish();
  assert.deepEqual(h.facts.failures, [h.path]); assert.deepEqual(h.facts.assets, []);
});

test("a complete old-document asset still gets verified after navigation", async () => {
  const h = capture(); h.respond(); h.page.emit("framenavigated", h.frame); await h.finish();
  assert.equal(h.facts.assets.length, 1); assert.deepEqual(h.facts.failures, []);
});

test("a missing local package asset remains a failure after navigation", async () => {
  const h = capture({ buildFile: () => { throw new Error("Missing package file"); } });
  h.respond(); h.page.emit("framenavigated", h.frame); await h.finish();
  assert.deepEqual(h.facts.failures, [h.path]); assert.deepEqual(h.facts.assets, []);
});
