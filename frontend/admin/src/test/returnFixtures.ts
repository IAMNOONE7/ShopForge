import type { OrderReturn } from "../api";
import { orderDetail } from "./orderFixtures";

export const returnRecord: OrderReturn = {
  id: "return-a", number: "RET-1", orderNumber: orderDetail.number, status: "Requested",
  requestedAt: "2026-10-12T10:00:00Z", refundedAmount: 0, reason: "The size did not fit.\nPlease review both items.",
  decidedAt: null, receivedAt: null, refundSettledAt: null,
  lines: [
    { storeProductId: "11111111-1111-1111-1111-111111111111", variantId: "22222222-2222-2222-2222-222222222222", productName: "Saved product name", quantity: 2 },
    { storeProductId: "11111111-1111-1111-1111-111111111111", variantId: "33333333-3333-3333-3333-333333333333", productName: "Saved product name", quantity: 1 },
  ],
  refund: null, refundProblem: null, refundNote: null,
};
export function returnOrder(records: OrderReturn[] = [returnRecord]) {
  return { ...orderDetail, returns: records.map((record) => ({ id: record.id, number: record.number, status: record.status, refundedAmount: record.refundedAmount, refund: record.refund, refundProblem: record.refundProblem })) };
}
