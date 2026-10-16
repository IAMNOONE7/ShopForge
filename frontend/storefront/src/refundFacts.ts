import type { Returns } from "./account";
import { HttpError } from "./api/http";
import type { Order } from "./cart";

export type CustomerRefund = {
  returnNumber: string;
  credited: number;
  state: string;
  settledAt: string | null;
};

export function knownRefundState(state: unknown) {
  return state === "Settled" || state === "InProgress" || state === "ByStore" ? state : "unknown";
}

export function recordedDate(value: unknown): string | null {
  return typeof value === "string" && /^\d{4}-\d{2}-\d{2}T/.test(value) && Number.isFinite(Date.parse(value)) ? value : null;
}

export function withOrderFacts(order: Order): Order {
  if (!order || typeof order !== "object") throw new HttpError("invalid-response", 200);
  // Older responses have no settlement facts; their credit status remains unconfirmed.
  const refunds: unknown = order.refunds === undefined ? [] : order.refunds;
  if (!Array.isArray(refunds)) throw new HttpError("invalid-response", 200);
  const seen = new Set<string>();
  return {
    ...order,
    paidAt: recordedDate(order.paidAt),
    refunds: refunds.map((value: unknown) => {
      if (value === null || typeof value !== "object") throw new HttpError("invalid-response", 200);
      const row = value as Record<string, unknown>;
      if (typeof row.returnNumber !== "string" || !row.returnNumber.trim() || seen.has(row.returnNumber)
        || typeof row.credited !== "number" || !Number.isFinite(row.credited) || row.credited < 0
        || typeof row.state !== "string") throw new HttpError("invalid-response", 200);
      seen.add(row.returnNumber);
      return { returnNumber: row.returnNumber, credited: row.credited, state: row.state,
        settledAt: row.state === "Settled" ? recordedDate(row.settledAt) : null };
    }),
  };
}

export function withReturnFacts(answer: Returns): Returns {
  if (!answer || !Array.isArray(answer.returns)) throw new HttpError("invalid-response", 200);
  return { ...answer, returns: answer.returns.map(returned => {
    if (!returned || !Number.isFinite(returned.refundedAmount) || returned.refundedAmount < 0
      || (returned.refundState != null && typeof returned.refundState !== "string")) throw new HttpError("invalid-response", 200);
    return { ...returned, refundState: returned.refundState ?? null,
      refundSettledAt: returned.refundState === "Settled" ? recordedDate(returned.refundSettledAt) : null };
  }) };
}
