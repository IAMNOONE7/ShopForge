import { afterEach, expect, it, vi } from "vitest";
import { consentsApi } from "./consents";
import { onUnauthorized } from "./http";
const unanswered = { purpose: "Marketing", isGranted: false, statement: '  Store wording\n<script>literal</script> Žluťoučký.  ', decidedAt: null };
const granted = { ...unanswered, isGranted: true, decidedAt: "2026-10-27T12:34:56Z" };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it("keeps literal purpose, wording and decision facts, including unanswered and unknown purposes", async () => {
  const other = { ...unanswered, purpose: "FuturePurpose" };
  const fetch = vi.fn().mockResolvedValue(json([{ ...unanswered, privateField: "not-a-consent-field" }, other])); vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal;
  expect(await consentsApi.list(signal)).toEqual([unanswered, other]);
  expect(fetch).toHaveBeenCalledExactlyOnceWith("/api/storefront/account/consents", expect.objectContaining({ method: "GET", body: undefined, signal, cache: "no-store" }));
});
it.each([true, false])("sends only the explicit boolean %s and accepts the returned current record", async isGranted => {
  const row = { ...granted, isGranted }, fetch = vi.fn().mockResolvedValue(json(row)); vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal; expect(await consentsApi.setMarketing(isGranted, signal)).toEqual(row);
  expect(fetch).toHaveBeenCalledExactlyOnceWith("/api/storefront/account/consents/marketing", expect.objectContaining({ method: "PUT", body: JSON.stringify({ isGranted }), signal, cache: "no-store" }));
});
it.each([null, {}, [null], [{ ...unanswered, purpose: " " }], [{ ...unanswered, statement: "" }], [{ ...unanswered, isGranted: "false" }], [{ ...unanswered, decidedAt: undefined }], [{ ...granted, decidedAt: "not-a-date" }], [{ ...granted, decidedAt: null }], [unanswered, unanswered]])("rejects malformed or contradictory consent readings: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value)));
  await expect(consentsApi.list(new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("accepts an empty list without inventing a default consent", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json([]))); expect(await consentsApi.list(new AbortController().signal)).toEqual([]);
});
it.each([null, [], unanswered, { ...granted, purpose: "FuturePurpose" }, { ...granted, isGranted: false }])("does not infer a saved grant from an incorrect mutation answer: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value)));
  await expect(consentsApi.setMarketing(true, new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([400, 401, 403, 404, 409, 429, 503])("preserves status %s while stripping private diagnostics, with no global invalidation or replay", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ detail: "private-wording", traceId: "private-reference", errors: { isGranted: ["private-record"] } }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "10" } })); vi.stubGlobal("fetch", fetch);
  const listener = vi.fn(), unsubscribe = onUnauthorized(listener);
  try {
    for (const action of [() => consentsApi.list(new AbortController().signal), () => consentsApi.setMarketing(false, new AbortController().signal)]) {
      const error = await action().catch((value: unknown) => value); expect(error).toMatchObject({ kind: "http", status, problem: null, traceId: null, retryAfter: "10" }); expect(JSON.stringify(error)).not.toContain("private-");
    }
    expect(fetch).toHaveBeenCalledTimes(2); expect(listener).not.toHaveBeenCalled();
  } finally { unsubscribe(); }
});
it.each(["{", "<html>signed out</html>"])("rejects malformed response bytes without exposing them: %s", async body => {
  vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => new Response(body, { headers: { "Content-Type": body[0] === "<" ? "text/html" : "application/json" } })));
  await expect(consentsApi.list(new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
  await expect(consentsApi.setMarketing(true, new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("drops an aborted response even if a transport still resolves it", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json([unanswered]))); const controller = new AbortController(); controller.abort();
  await expect(consentsApi.list(controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
