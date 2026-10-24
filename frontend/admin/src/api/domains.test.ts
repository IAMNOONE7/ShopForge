// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { addStoreDomain, domainNeedsRefresh, getStoreDomains, makeStoreDomainPrimary, removeStoreDomain, startStoreDomainZone, verifyStoreDomain, type StoreDomain } from "./domains";
import { normalizedDomain, domainProblem } from "../components/domainValidation";
import { HttpError } from "./http";

const pending = { edge: null, zone: null, id: "domain-a", hostName: "shop.example.cz", isPrimary: false, isVerified: false,
  verifiedAt: null, challengeName: "_shopforge-challenge.shop.example.cz", challengeValue: "proof", claimExpiresAt: "2030-01-01T00:00:00Z" };
const verified = { ...pending, isVerified: true, verifiedAt: "2026-10-15T00:00:00Z", challengeName: null, challengeValue: null, claimExpiresAt: null };
const id = "11111111-1111-1111-1111-111111111111";
const edge = { state: "binding", operationId: id, recordsByPlatform: false, validation: "pendingRevalidation", records: [{ type: "TXT", name: "_dnsauth.shop.example.cz", value: '<script>literal</script>\nexact value', purpose: "validation" }, { type: "CNAME", name: "shop.example.cz", value: "edge.example.test", purpose: "traffic" }] };
const zone = { name: "example.cz", state: "waiting", nameServers: ["one.example.test", "two.example.test"], operationId: id };
function response(value: unknown) { return new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } }); }
afterEach(() => { vi.unstubAllGlobals(); });

describe("domain contracts", () => {
  it.each([null, "example.cz"])("starts the scoped zone operation with zone=%s and a checked ID", async zone => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ operationId: id, extra: "not public" }), { status: 202, headers: { "Content-Type": "application/json" } }));
    vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
    await expect(startStoreDomainZone("store/a", "domain/b", zone, signal)).resolves.toEqual({ operationId: id });
    expect(fetch).toHaveBeenCalledExactlyOnceWith("/api/admin/stores/store%2Fa/domains/domain%2Fb/zone", expect.objectContaining({ method: "POST", body: JSON.stringify({ zone }), signal }));
    expect(fetch.mock.calls[0][1].headers["Idempotency-Key"]).toBeUndefined();
  });
  it.each([null, [], {}, { operationId: null }, { operationId: "" }, { operationId: "wrong" }, { operationId: 1 }])("refuses an unconfirmed zone operation ID: %j", async value => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(value)));
    await expect(startStoreDomainZone("store", "domain", null)).rejects.toMatchObject({ kind: "invalid-response", status: 202 });
  });
  it("preserves a zone refusal without retrying and rejects a late aborted acceptance", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ title: "Not now", detail: "Zone work is running." }), { status: 409, headers: { "Content-Type": "application/problem+json" } }))
      .mockResolvedValueOnce(response({ operationId: id }));
    vi.stubGlobal("fetch", fetch);
    await expect(startStoreDomainZone("store", "domain", "example.cz")).rejects.toMatchObject({ status: 409, problem: { title: "Not now" } });
    const controller = new AbortController(); controller.abort();
    await expect(startStoreDomainZone("store", "domain", null, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it("uses the real scoped list/add/verify/primary/delete contracts with empty action bodies", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(response([pending]))
      .mockResolvedValueOnce(response(pending)).mockResolvedValueOnce(response(verified))
      .mockResolvedValueOnce(response({ ...verified, isPrimary: true })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetch);
    const signal = new AbortController().signal;
    await expect(getStoreDomains("store/a", signal)).resolves.toEqual([pending]);
    await addStoreDomain("store/a", pending.hostName);
    await verifyStoreDomain("store/a", pending.id);
    await makeStoreDomainPrimary("store/a", pending.id);
    await removeStoreDomain("store/a", pending.id);
    expect(fetch.mock.calls.map(([url, init]) => [url, init.method])).toEqual([
      ["/api/admin/stores/store%2Fa/domains", "GET"], ["/api/admin/stores/store%2Fa/domains", "POST"],
      ["/api/admin/stores/store%2Fa/domains/domain-a/verify", "POST"], ["/api/admin/stores/store%2Fa/domains/domain-a/primary", "POST"],
      ["/api/admin/stores/store%2Fa/domains/domain-a", "DELETE"],
    ]);
    expect(fetch.mock.calls[0][1].signal).toBe(signal);
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ hostName: pending.hostName });
    expect(fetch.mock.calls[2][1].body).toBeUndefined();
    expect(fetch.mock.calls[3][1].body).toBeUndefined();
  });

  it.each([{}, [pending, pending], [{ ...pending, claimExpiresAt: "yesterday" }], [{ ...pending, isPrimary: "false" }],
    [{ ...pending, challengeValue: null }], [{ ...pending, verifiedAt: undefined }]])("refuses malformed/duplicate domain data: %j", async (value) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(value)));
    await expect(getStoreDomains("store")).rejects.toMatchObject({ kind: "invalid-response" });
  });

  it("does not confirm mismatched or unapplied mutation responses", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(response({ ...pending, hostName: "other.cz" }))
      .mockResolvedValueOnce(response(pending)).mockResolvedValueOnce(response({ ...verified, id: "other" }))
      .mockResolvedValueOnce(response(verified)).mockResolvedValueOnce(response({})));
    await expect(addStoreDomain("store", pending.hostName)).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(verifyStoreDomain("store", pending.id)).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(verifyStoreDomain("store", pending.id)).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(makeStoreDomainPrimary("store", pending.id)).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(removeStoreDomain("store", pending.id)).rejects.toMatchObject({ kind: "invalid-response" });
  });

  it("normalizes case, root dots and IDN without treating input as a URL", () => {
    expect(normalizedDomain("  SHOP.Example.CZ. ")).toBe("shop.example.cz");
    expect(normalizedDomain("Žluťoučký.cz")).toBe("xn--luouk-uva4it5a4g.cz");
    expect(normalizedDomain("x".repeat(63) + ".cz")).toBeTruthy();
    const max = ["a".repeat(63), "b".repeat(63), "c".repeat(63), "d".repeat(61)].join(".");
    expect(max.length).toBe(253);
    expect(normalizedDomain(max)).toBe(max);
    for (const input of ["", "  ", "https://shop.cz", "shop.cz:443", "user@shop.cz", "shop.cz/path", "shop.cz?x", "shop.cz#x", "shop%2ecz", "with space.cz", "a..cz", "-shop.cz", "shop_.cz", "x".repeat(64) + ".cz", max + "a"]) {
      expect(normalizedDomain(input)).toBeNull();
    }
  });

  it("maps only the known operation-specific refusals, leaving unknown errors generic", () => {
    expect(domainProblem(new HttpError("http", 409, { title: "The record is not there yet" }), "verify")).toBe("dnsPending");
    expect(domainProblem(new HttpError("http", 409, { title: "The record is not there yet" }), "add")).toBeNull();
    expect(domainProblem(new HttpError("http", 409, { title: "Other conflict" }), "add")).toBeNull();
    expect(domainProblem(new HttpError("http", 400, { errors: { hostName: ["A name under localhost belongs to the platform. Your store already has one."] } }), "add")).toBe("platformAddress");
  });

  it("checks nullable edge/zone contracts on list and every returned domain without leaking undeclared fields", async () => {
    const item = { ...verified, edge, zone }, raw = { ...item, input: "private", edge: { ...edge, secret: "private", records: edge.records.map(record => ({ ...record, extra: "private" })) }, zone: { ...zone, extra: "private" } };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(response([raw])).mockResolvedValueOnce(response(raw)).mockResolvedValueOnce(response(raw)).mockResolvedValueOnce(response({ ...raw, isPrimary: true })));
    expect(await getStoreDomains("store")).toEqual([item]); expect(await addStoreDomain("store", item.hostName)).toEqual(item);
    expect(await verifyStoreDomain("store", item.id)).toEqual(item); expect(await makeStoreDomainPrimary("store", item.id)).toEqual({ ...item, isPrimary: true });
  });
  it("preserves unknown states/purposes/provider validation and empty DNS strings without coercion", async () => {
    const item = { ...verified, edge: { ...edge, state: "future", validation: "", operationId: null, records: [{ type: "", name: "", value: "", purpose: "future" }] }, zone: { ...zone, state: "future", nameServers: [], operationId: null } };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response([item]))); expect(await getStoreDomains("store")).toEqual([item]);
  });
  it.each([
    { edge: undefined }, { zone: undefined }, { edge: {} }, { zone: [] },
    { edge: { ...edge, state: null } }, { edge: { ...edge, recordsByPlatform: "false" } }, { edge: { ...edge, validation: false } }, { edge: { ...edge, operationId: "wrong" } },
    { edge: { ...edge, records: null } }, { edge: { ...edge, records: [{ ...edge.records[0], name: null }] } },
    { edge: { ...edge, records: [{ ...edge.records[0], value: 0 }] } }, { edge: { ...edge, records: [{ ...edge.records[0], purpose: "" }] } },
    { zone: { ...zone, name: "" } }, { zone: { ...zone, state: false } }, { zone: { ...zone, nameServers: null } }, { zone: { ...zone, nameServers: [false] } }, { zone: { ...zone, operationId: "wrong" } },
  ])("refuses malformed edge or zone facts (%j) before they can authorize changes", async change => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response([{ ...verified, ...change }]))); await expect(getStoreDomains("store")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("passes cancellation to every existing mutation without adding endpoints, bodies or automatic retries", async () => {
    const signal = new AbortController().signal, fetch = vi.fn().mockResolvedValueOnce(response(pending)).mockResolvedValueOnce(response(verified)).mockResolvedValueOnce(response({ ...verified, isPrimary: true })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetch); await addStoreDomain("store", pending.hostName, signal); await verifyStoreDomain("store", pending.id, signal); await makeStoreDomainPrimary("store", verified.id, signal); await removeStoreDomain("store", pending.id, signal);
    expect(fetch).toHaveBeenCalledTimes(4); expect(fetch.mock.calls.every(call => call[1].signal === signal)).toBe(true); expect(fetch.mock.calls.slice(1).every(call => call[1].body === undefined && call[1].headers === undefined)).toBe(true);
  });
  it("refuses an aborted read or mutation even if transport answers", async () => {
    const controller = new AbortController(); controller.abort(); const fetch = vi.fn().mockResolvedValueOnce(response([pending])).mockResolvedValueOnce(response(verified)); vi.stubGlobal("fetch", fetch);
    await expect(getStoreDomains("store", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
    await expect(verifyStoreDomain("store", pending.id, controller.signal)).rejects.toMatchObject({ name: "AbortError" }); expect(fetch).toHaveBeenCalledTimes(2);
  });
  it("polls known unfinished edge/delegation facts without treating validation as live readiness", () => {
    for (const state of ["waiting", "binding", "unbinding"]) expect(domainNeedsRefresh({ ...verified, edge: { ...edge, state } })).toBe(true);
    expect(domainNeedsRefresh({ ...verified, edge: { ...edge, state: "bound", validation: "pendingRevalidation" } })).toBe(true);
    expect(domainNeedsRefresh({ ...verified, zone })).toBe(true);
    for (const item of [verified, { ...verified, edge: { ...edge, state: "future" } }, { ...verified, edge: { ...edge, state: "bound", validation: "approved" } }, { ...verified, zone: { ...zone, state: "active" } }]) expect(domainNeedsRefresh(item as StoreDomain)).toBe(false);
  });
});
