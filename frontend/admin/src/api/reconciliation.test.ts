import { afterEach, describe, expect, it, vi } from "vitest";
import { calendarDay, isReportRange, reconciliationApi, reportDate, reportQuery } from "./reconciliation";
import { reconciliationFixture } from "../test/reconciliationFixtures";

const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
describe("payment report contracts", () => {
  it("reads the source-selected default, then reuses only its returned provider on refresh", async () => {
    const data = reconciliationFixture(), signal = new AbortController().signal;
    const fetch = vi.fn().mockResolvedValueOnce(reply({ ...data, future: true })).mockResolvedValueOnce(reply(data)); vi.stubGlobal("fetch", fetch);
    const result = await reconciliationApi.read("store/a", null, signal);
    await reconciliationApi.read("store/a", { from: data.from, to: data.to }, signal, result.provider);
    expect(fetch.mock.calls.map(([url, init]) => [url, init.method, init.body, init.signal])).toEqual([
      ["/api/admin/stores/store%2Fa/reconciliation", "GET", undefined, signal],
      ["/api/admin/stores/store%2Fa/reconciliation?from=2026-10-01&to=2026-10-14&provider=comgate", "GET", undefined, signal],
    ]);
  });
  it.each([
    null, {}, { provider: "" }, { timeZone: null }, { from: "2026-02-30" }, { testPayments: -1 }, { unexplained: 99 },
    { statementsThrough: "not-a-day" }, { days: [] }, { differences: [{}] },
  ])("rejects malformed or inconsistent source facts: %j", async change => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(change === null ? null : Object.keys(change).length === 0 ? {} : { ...reconciliationFixture(), ...change })));
    await expect(reconciliationApi.read("store", null)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("rejects the wrong provider or requested period without another request", async () => {
    const fetch = vi.fn().mockImplementation(async () => reply(reconciliationFixture())); vi.stubGlobal("fetch", fetch);
    await expect(reconciliationApi.read("store", null, undefined, "other")).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(reconciliationApi.read("store", { from: "2026-10-02", to: "2026-10-14" })).rejects.toMatchObject({ kind: "invalid-response" });
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it.each(["date", "paid", "paidAmount", "settled", "fees", "transferred", "unexplained"])("rejects an invalid daily %s", async field => {
    const data = reconciliationFixture(); Object.assign(data.days[0]!, { [field]: field === "date" ? "2026-10-02" : "0" });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(data)));
    await expect(reconciliationApi.read("store", null)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("keeps unknown kinds, nullable reference amounts and signed source amounts without inventing interpretation", async () => {
    const data = reconciliationFixture(); data.statementsThrough = null;
    Object.assign(data.differences[0]!, { kind: "future-kind", expected: null, settled: -0.15, currency: null, orderNumber: null, transactionId: null });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(data)));
    expect(await reconciliationApi.read("store", null)).toEqual(data);
  });
  it.each([400, 401, 403, 404, 429, 503])("preserves a %s read refusal without retrying or supplying a zero report", async status => {
    const fetch = vi.fn().mockResolvedValue(reply({}, status)); vi.stubGlobal("fetch", fetch);
    await expect(reconciliationApi.read("store", null)).rejects.toMatchObject({ status }); expect(fetch).toHaveBeenCalledTimes(1);
  });
  it("rejects an aborted reply", async () => {
    const controller = new AbortController(); controller.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(reconciliationFixture())));
    await expect(reconciliationApi.read("store", null, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
  });
});
describe("provider calendar periods", () => {
  it.each([
    ["2026-10-01", "2026-10-01", true], ["2026-10-01", "2026-10-14", true], ["2026-10-01", "2026-10-31", true],
    ["2026-10-01", "2026-11-01", false], ["2026-10-02", "2026-10-01", false], ["2026-02-30", "2026-03-01", false],
    ["2028-02-29", "2028-03-01", true], ["0000-01-01", "0000-01-01", false], ["2026-03-28", "2026-03-30", true],
  ])("validates inclusive bounds %s to %s", (from, to, expected) => expect(isReportRange({ from, to })).toBe(expected));
  it.each(["from=2026-10-01", "from=&to=", "from=2026-10-01&from=2026-10-02&to=2026-10-14"])("does not request an ambiguous URL: %s", query => expect(reportQuery(new URLSearchParams(query)).invalid).toBe(true));
  it("keeps default periods server-owned and calendar formatting independent of the provider or browser offset", () => {
    expect(reportQuery(new URLSearchParams())).toEqual({ range: null, invalid: false });
    expect(calendarDay("2026-03-30")! - calendarDay("2026-03-28")!).toBe(2);
    expect(reportDate("2026-10-01", "en-GB")).toBe("1 Oct 2026");
  });
});
