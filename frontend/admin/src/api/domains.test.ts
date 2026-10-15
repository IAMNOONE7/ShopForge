// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { addStoreDomain, getStoreDomains, makeStoreDomainPrimary, removeStoreDomain, verifyStoreDomain } from "./domains";
import { normalizedDomain, domainProblem } from "../components/domainValidation";
import { HttpError } from "./http";

const pending = { id: "domain-a", hostName: "shop.example.cz", isPrimary: false, isVerified: false,
  verifiedAt: null, challengeName: "_shopforge-challenge.shop.example.cz", challengeValue: "proof", claimExpiresAt: "2030-01-01T00:00:00Z" };
const verified = { ...pending, isVerified: true, verifiedAt: "2026-10-15T00:00:00Z", challengeName: null, challengeValue: null, claimExpiresAt: null };
function response(value: unknown) { return new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } }); }
afterEach(() => { vi.unstubAllGlobals(); });

describe("domain contracts", () => {
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
});
