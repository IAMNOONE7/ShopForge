// @vitest-environment jsdom
import { afterEach, describe, expect, it } from "vitest";
import { HttpError } from "../api/http";
import { orderDetail, returnedOrder } from "../test/orderFixtures";
import { returnRecord } from "../test/returnFixtures";
import { canChangeRefund, canRecoverRefund, makeRefundAttempt, readRefundAttempt, refundNeedsRecovery, refundStorageKey } from "./refundWorkflow";
const order = { ...orderDetail, status: "Paid" };
afterEach(() => sessionStorage.clear());
describe("refund eligibility and recovery", () => {
  it.each(["Paid", "Shipped"])("receives an accepted return of a %s order only before receipt/refund was recorded", (status) => {
    expect(canChangeRefund("receive", { ...order, status }, { ...returnRecord, status: "Accepted" })).toBe(true);
    expect(canChangeRefund("receive", { ...order, status }, { ...returnRecord, status: "Received" })).toBe(false);
  });
  it.each(["AwaitingPayment", "Cancelled", "Refunded", "Future"])("does not receive or refund order status %s", (status) => {
    expect(canChangeRefund("receive", { ...order, status }, { ...returnRecord, status: "Accepted" })).toBe(false);
    expect(canChangeRefund("order-refund", { ...order, status })).toBe(false);
  });
  it.each([
    [null, false, false], ["Pending", false, true], ["Failed", true, true], ["ByStore", false, false], ["Refunded", false, false], ["ClosedByHand", false, false], ["Future", false, false],
  ])("keeps retry/closure eligibility separate for refund %s", (refund, retry, close) => {
    const row = { ...returnRecord, status: "Received", refund };
    expect(canChangeRefund("retry", order, row)).toBe(retry); expect(canChangeRefund("close", order, row)).toBe(close);
  });
  it("requires the real order association and a full current return record", () => {
    expect(canChangeRefund("retry", order, { ...returnRecord, orderNumber: "OTHER", status: "Received", refund: "Failed" })).toBe(false);
    expect(canRecoverRefund(makeRefundAttempt("receive", order, { ...returnRecord, status: "Accepted" }), order, null)).toBe(false);
  });
  it("will not replay an order refund after a new credit even if the order remains Paid", () => {
    const before = { ...order, returns: [returnedOrder] }; const attempt = makeRefundAttempt("order-refund", before);
    expect(canRecoverRefund(attempt, before)).toBe(true);
    expect(canRecoverRefund(attempt, { ...before, returns: [...before.returns, { ...returnedOrder, id: "new-credit" }] })).toBe(false);
  });
  it("retains a tab-scoped immutable key, original return IDs, currency and exact trimmed note", () => {
    const attempt = makeRefundAttempt("close", order, { ...returnRecord, status: "Received", refund: "Pending" }, "  Paid\nexternally  ");
    const key = refundStorageKey("user/a", "store/b"); sessionStorage.setItem(key, JSON.stringify(attempt));
    expect(readRefundAttempt(key)).toEqual({ ...attempt, note: "Paid\nexternally" }); expect(key).not.toBe(refundStorageKey("other", "store/b"));
    expect(readRefundAttempt(refundStorageKey("user/a", "other"))).toBeNull();
    expect(canRecoverRefund({ ...attempt, reviewOnly: true }, order, { ...returnRecord, status: "Received", refund: "Pending" })).toBe(false);
  });
  it.each(["bad-json", JSON.stringify({}), JSON.stringify({ ...makeRefundAttempt("order-refund", order), previousReturnIds: ["same", "same"] })])("blocks malformed saved requests", (saved) => {
    sessionStorage.setItem("request", saved); expect(() => readRefundAttempt("request")).toThrow();
  });
  it("keeps uncertain answers, in-flight requests and key mismatch for review, but not definite refusals", () => {
    for (const error of [new HttpError("network", null), new HttpError("invalid-response", 200), new HttpError("http", 502), new HttpError("http", 409, { title: "A request with this key is still in progress" }), new HttpError("http", 422, { title: "This idempotency key was used for a different request" })]) expect(refundNeedsRecovery(error)).toBe(true);
    expect(refundNeedsRecovery(new HttpError("http", 409, { title: "The refund was refused" }))).toBe(false);
    expect(refundNeedsRecovery(new HttpError("http", 400))).toBe(false);
  });
});
