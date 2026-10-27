import { afterEach, expect, it, vi } from "vitest";
import { privacyApi } from "./privacy";
import { onUnauthorized } from "./http";
const email = "ada@example.test";
const envelope = { exportedAt: "2026-10-27T10:00:00Z", sections: { account: [{ email }], wishlist: [] } };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it("downloads the original JSON bytes, including unknown sections and precise numbers, with a fixed safe filename", async () => {
  const bytes = '{"exportedAt":"2026-10-27T10:00:00Z","sections":{"account":[{"email":"ada@example.test"}],"wishlist":[],"future-module":[{"amount":9007199254740993.123456,"nested":{"value":"Příliš žluťoučký"}}]}}';
  const fetch = vi.fn().mockResolvedValue(new Response(bytes, { headers: { "Content-Type": "application/json; charset=utf-8", "Content-Disposition": 'attachment; filename="../private.html"' } })); vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal, result = await privacyApi.export(email, signal);
  expect(await result.blob.text()).toBe(bytes); expect(result.filename).toBe("shopforge-data.json");
  expect(result.sections).toEqual([{ name: "account", entries: 1 }, { name: "wishlist", entries: 0 }, { name: "future-module", entries: 1 }]);
  expect(fetch).toHaveBeenCalledExactlyOnceWith("/api/storefront/account/export", expect.objectContaining({ method: "GET", cache: "no-store", signal, body: undefined }));
});
it.each([
  null, {}, { ...envelope, exportedAt: "not-a-date" }, { ...envelope, sections: [] },
  { ...envelope, sections: { wishlist: [] } }, { ...envelope, sections: { account: [] } },
  { ...envelope, sections: { account: [{ email }, { email }] } }, { ...envelope, sections: { account: [{ email: "other@example.test" }] } },
  { ...envelope, sections: { account: [{ email }], unknown: {} } },
])("rejects an invalid archive envelope or owner before a browser download: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value)));
  await expect(privacyApi.export(email, new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([['{', 'application/json'], ['<html>login</html>', 'text/html'], ['', 'application/json']])("rejects malformed and non-JSON export content: %s", async (body, type) => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body, { headers: { "Content-Type": type } })));
  await expect(privacyApi.export(email, new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("does not release an export after cancellation even if the transport finishes", async () => {
  const controller = new AbortController(); vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(envelope))); controller.abort();
  await expect(privacyApi.export(email, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it("sends the raw password only in the deletion body and requires the documented 204", async () => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 })); vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal; await privacyApi.erase(" raw password ", signal);
  expect(fetch).toHaveBeenCalledExactlyOnceWith("/api/storefront/account/delete", expect.objectContaining({ method: "POST", body: '{"password":" raw password "}', signal }));
});
it.each([200, 202, 205])("does not treat an undocumented empty %s as a confirmed deletion", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status })));
  await expect(privacyApi.erase("password", new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response", status });
});
it.each([400, 401, 403, 404, 409, 429, 500])("preserves refusal status %s without leaking server details, replaying or invalidating the session implicitly", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ detail: "private-data", traceId: "private-reference", errors: { password: ["private-password"] } }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "20" } })); vi.stubGlobal("fetch", fetch);
  const listener = vi.fn(), unsubscribe = onUnauthorized(listener);
  try {
    for (const action of [() => privacyApi.export(email, new AbortController().signal), () => privacyApi.erase("password", new AbortController().signal)]) {
      const error = await action().catch((value: unknown) => value); expect(error).toMatchObject({ status, problem: null, traceId: null, retryAfter: "20" }); expect(JSON.stringify(error)).not.toContain("private-");
    }
    expect(fetch).toHaveBeenCalledTimes(2); expect(listener).not.toHaveBeenCalled();
  } finally { unsubscribe(); }
});
