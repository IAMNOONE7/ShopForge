import { api } from "../api";
import { HttpError } from "../api/http";

export async function loadReturnReview(storeId: string, id: string, signal: AbortSignal) {
  const record = await api.returnDetail(storeId, id, signal);
  signal.throwIfAborted();
  if (record.id.toLowerCase() !== id.toLowerCase()) throw new HttpError("invalid-response", 200);
  const order = await api.order(storeId, record.orderNumber, signal);
  const summary = order.returns.find(item => item.id === record.id);
  // These reads can straddle receipt or settlement. Conflicting facts must not enable another money/stock action.
  if (order.number !== record.orderNumber || !summary || summary.number !== record.number ||
    summary.status !== record.status || summary.refund !== record.refund || summary.refundedAmount !== record.refundedAmount)
    throw new HttpError("invalid-response", 200);
  return { record, order, summary };
}
