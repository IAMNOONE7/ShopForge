import { expect, it } from "vitest";
import { platformPlan } from "../test/platformPlanFixtures";
import { planDraft, reviewPlan } from "./planDraft";

it("distinguishes empty unlimited caps, zero and the maximum Int32 value", () => {
  expect(planDraft(platformPlan)).toMatchObject({ maxStores: "", maxProducts: "0" });
  expect(reviewPlan({ code: " Growth ", name: " Growth ", maxStores: "0", maxProducts: "2147483647" }, null)).toEqual({ values: { code: "growth", name: "Growth", maxStores: 0, maxProducts: 2147483647 }, issues: [] });
});
it.each(["-1", "1.5", "1e2", "+1", "Infinity", "2147483648", "１２", "0x10"])("refuses a cap that cannot bind to an Int32: %s", value => {
  expect(reviewPlan({ code: "ok", name: "Good", maxStores: value, maxProducts: value }, null).issues).toEqual(["maxStores", "maxProducts"]);
});
it("keeps the immutable saved code and normalizes only new codes", () => {
  expect(reviewPlan({ code: "changed", name: " Name ", maxStores: "", maxProducts: "" }, platformPlan).values).toEqual({ code: "scale", name: "Name", maxStores: null, maxProducts: null });
});
it("checks normalized code storage length and trimmed UTF-16 name length", () => {
  expect(reviewPlan({ code: "İ".repeat(40), name: "Good", maxStores: "", maxProducts: "" }, null).issues).toContain("code");
  expect(reviewPlan({ code: "𐐨".repeat(40), name: "x".repeat(100), maxStores: "", maxProducts: "" }, null).issues).toEqual([]);
  expect(reviewPlan({ code: "x".repeat(41), name: "x".repeat(101), maxStores: "", maxProducts: "" }, null).issues).toEqual(["code", "name"]);
});
