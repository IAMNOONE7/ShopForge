import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { HttpError } from "./http";
import { publicationRefusal } from "./readiness";

const check = { check: "orders.payment-connected", isReady: false, problem: "Card payments have no usable account.", weight: "required", blocks: true };
afterEach(() => vi.unstubAllGlobals());

describe("server readiness contract", () => {
  it("reads the store-scoped checklist and camel-case weights with an abort signal", async () => {
    const fetch = vi.fn().mockResolvedValue(Response.json({ isReady: false, checks: [check] }));
    vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
    expect(await api.storeReadiness("store-a", signal)).toEqual({ isReady: false, checks: [check] });
    expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store-a/readiness");
    expect(fetch.mock.calls[0][1]).toMatchObject({ method: "GET", signal });
  });

  it.each([null, {}, { isReady: true, checks: null }, { isReady: "true", checks: [] },
    { isReady: true, checks: [{ ...check, isReady: "false" }] },
    { isReady: true, checks: [{ ...check, weight: "futureWeight" }] }])("rejects malformed readiness instead of claiming it is ready: %j", async (body) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(body)));
    await expect(api.storeReadiness("store-a")).rejects.toMatchObject({ kind: "invalid-response" });
  });

  it("retains unknown check names and explicit false values", async () => {
    const body = { isReady: false, checks: [{ ...check, check: "future.check", problem: null }] };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(body)));
    expect(await api.storeReadiness("store-a")).toEqual(body);
  });

  it("uses the publication refusal checklist and falls back to prose when its shape is unavailable", () => {
    expect(publicationRefusal(new HttpError("http", 409, { readiness: [check], problems: [check.problem] }))).toEqual({ checks: [check], problems: [check.problem] });
    expect(publicationRefusal(new HttpError("http", 409, { readiness: [{ ...check, weight: 0 }], problems: [check.problem] }))).toEqual({ checks: null, problems: [check.problem] });
    expect(publicationRefusal(new HttpError("http", 409))).toBeNull();
    expect(publicationRefusal(new HttpError("http", 403, { readiness: [check] }))).toBeNull();
  });
});
