import { afterEach, expect, it, vi } from "vitest";
import { setupProgressApi } from "./setupProgress";

const id = "11111111-1111-1111-1111-111111111111";
const summary = { id, kind: "email.domain", subject: null, state: "running", currentStep: "verification", createdAt: "2026-10-22T07:00:00Z", finishedAt: null };
const step = { name: "verification", status: "waiting", resourceId: null, created: false, requestId: "provider-request", keepsSecret: true, attempts: 0, checks: 0,
  waitingSince: "2026-10-22T07:01:00Z", note: "Publish the TXT record.", lastError: null, lastErrorAt: null, startedAt: null, finishedAt: null };
const { currentStep: _currentStep, ...base } = summary;
const detail = { ...base, stepRunning: false, changedAt: "2026-10-22T07:01:00Z", nextCheckAt: null, actions: ["cancel"], steps: [step] };
function answer(value: unknown, status = 200) { const fetch = vi.fn().mockResolvedValue(Response.json(value, { status })); vi.stubGlobal("fetch", fetch); return fetch; }
afterEach(() => vi.unstubAllGlobals());

it("reads the real latest-50 and direct-detail routes with cancellation, without writes or invented paging", async () => {
  const fetch = answer([summary]), signal = new AbortController().signal;
  expect(await setupProgressApi.list("store/a", signal)).toEqual([summary]);
  fetch.mockResolvedValueOnce(Response.json(detail)); expect(await setupProgressApi.detail("store/a", id, signal)).toEqual(detail);
  expect(fetch.mock.calls.map(call => call[0])).toEqual(["/api/admin/stores/store%2Fa/operations", `/api/admin/stores/store%2Fa/operations/${id}`]);
  expect(fetch.mock.calls.every(call => call[1].method === "GET" && call[1].signal === signal)).toBe(true);
});
it("preserves unknown states/kinds, nulls, zero counts and literal notes while discarding undeclared fields", async () => {
  const unknown = { ...detail, kind: "future.setup", state: "futureState", actions: ["futureAction"], steps: [{ ...step, status: "futureStep", note: '<script>unsafe</script>\nTXT value' }] };
  answer({ ...unknown, input: "private-input", secret: "private-secret", steps: unknown.steps.map(item => ({ ...item, secret: "private-secret" })) });
  expect(await setupProgressApi.detail("store", id)).toEqual(unknown);
});
it("accepts the real list bound and rejects duplicates, overflow and malformed roots", async () => {
  const rows = Array.from({ length: 50 }, (_, n) => ({ ...summary, id: `11111111-1111-1111-1111-${String(n).padStart(12, "0")}` }));
  const fetch = answer(rows); expect(await setupProgressApi.list("store")).toHaveLength(50);
  for (const value of [null, {}, [...rows, summary], [summary, { ...summary, id: id.toUpperCase() }]]) {
    fetch.mockResolvedValueOnce(Response.json(value)); await expect(setupProgressApi.list("store")).rejects.toMatchObject({ kind: "invalid-response" });
  }
});
it.each(["id", "kind", "subject", "state", "currentStep", "createdAt", "finishedAt"])("requires each declared summary field (%s)", async field => {
  const value: Record<string, unknown> = { ...summary }; delete value[field]; answer([value]);
  await expect(setupProgressApi.list("store")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([
  { id: "wrong" }, { kind: " " }, { state: null }, { subject: 1 }, { createdAt: "2026-10-22T07:00:00" }, { createdAt: "invalid" }, { finishedAt: false },
  { id: "22222222-2222-2222-2222-222222222222" }, { changedAt: null }, { stepRunning: 1 }, { nextCheckAt: "invalid" }, { actions: [4] }, { actions: null }, { steps: null },
].map(change => ({ change })))("rejects malformed or mismatched detail %j", async ({ change }) => {
  answer({ ...detail, ...change }); await expect(setupProgressApi.detail("store", id)).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([
  { name: "" }, { status: null }, { resourceId: 1 }, { requestId: {} }, { created: null }, { keepsSecret: "true" }, { attempts: -1 }, { checks: 1.5 },
  { waitingSince: "invalid" }, { note: {} }, { lastError: false }, { lastErrorAt: "2026-10-22" }, { startedAt: false }, { finishedAt: "invalid" },
].map(change => ({ change })))("rejects malformed step facts %j", async ({ change }) => {
  answer({ ...detail, steps: [{ ...step, ...change }] }); await expect(setupProgressApi.detail("store", id)).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([403, 404, 429, 503])("preserves a %i read failure and never retries it", async status => {
  const fetch = answer({ title: "Unavailable" }, status); await expect(setupProgressApi.detail("store", id)).rejects.toMatchObject({ kind: "http", status }); expect(fetch).toHaveBeenCalledOnce();
});
it("refuses an aborted read even if its transport returns a value", async () => {
  const controller = new AbortController(), fetch = answer([summary]); controller.abort();
  await expect(setupProgressApi.list("store", controller.signal)).rejects.toMatchObject({ name: "AbortError" }); expect(fetch).toHaveBeenCalledOnce();
});
