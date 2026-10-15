import type { OrderReturn } from "../api";
import { orderDetail } from "./orderFixtures";

export const returnRecord: OrderReturn = {
  id: "return-a", number: "RET-1", orderNumber: orderDetail.number, status: "Requested",
  requestedAt: "2026-10-12T10:00:00Z", refundedAmount: 0, reason: "The size did not fit.\nPlease review both items.",
  lines: [{ productName: "Saved product name", quantity: 2 }, { productName: "Saved product name", quantity: 1 }],
  refund: null, refundProblem: null, refundNote: null,
};
export function returnOrder(records: OrderReturn[] = [returnRecord]) {
  return { ...orderDetail, returns: records.map((record) => ({ id: record.id, number: record.number, status: record.status, refundedAmount: record.refundedAmount, refund: record.refund, refundProblem: record.refundProblem })) };
}
