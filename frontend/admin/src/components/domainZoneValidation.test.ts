import { describe, expect, it } from "vitest";
import { HttpError } from "../api/http";
import { domainZoneInput, domainZoneProblem } from "./domainZoneValidation";

describe("managed DNS zone selection", () => {
  it.each([
    ["shop.example.cz", "", { zone: null, name: "example.cz" }],
    ["example.cz", "  ", { zone: null, name: "example.cz" }],
    ["shop.example.cz", " SHOP.Example.CZ. ", { zone: "shop.example.cz", name: "shop.example.cz" }],
    ["shop.example.cz", "EXAMPLE.CZ", { zone: "example.cz", name: "example.cz" }],
    ["shop.example.co.uk", "", { zone: null, name: "co.uk" }],
    ["shop.example.co.uk", "example.co.uk", { zone: "example.co.uk", name: "example.co.uk" }],
    ["shop.žluťoučký.cz", "ŽLUŤOUČKÝ.CZ", { zone: "xn--luouk-uva4it5a4g.cz", name: "xn--luouk-uva4it5a4g.cz" }],
  ])("reviews the exact backend zone for %s and %s", (host, draft, expected) => {
    expect(domainZoneInput(host, draft)).toEqual(expected);
  });
  it.each(["cz", "other.cz", "ample.cz", "notexample.cz", "child.shop.example.cz", "https://example.cz", "example.cz:443", "example.cz/path", "example.cz#x", "a..cz", "-example.cz", "a".repeat(64) + ".cz"])("rejects a zone outside the supported host/name contract: %s", draft => {
    expect(domainZoneInput("shop.example.cz", draft)).toBeNull();
  });
  it("does not turn an invalid or single-label target into a default zone", () => {
    expect(domainZoneInput("localhost", "")).toBeNull();
    expect(domainZoneInput("https://shop.example.cz", "")).toBeNull();
  });
  it.each([
    [404, "Not Found", "domainMissing"], [409, "Not here", "unavailable"],
    [409, "That zone is already taken", "taken"], [409, "Not now", "busy"],
    [409, "Unknown conflict", null], [403, "Not here", null],
  ])("maps only the known refusal %s / %s", (status, title, expected) => {
    expect(domainZoneProblem(new HttpError("http", status, { title }))).toBe(expected);
  });
});
