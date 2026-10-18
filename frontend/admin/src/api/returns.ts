import type { OrderReturn } from "../api";
import { refundStatuses, returnLimit, returnStatuses, type ReturnQuery } from "../utils/returnQuery";
import { HttpError, requestJson } from "./http";

function isReturn(value: unknown): value is OrderReturn {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
  const item = value as Record<string, unknown>;
  const text = (value: unknown) => typeof value === "string" && value.trim().length > 0;
  const nullableText = (value: unknown) => value === null || typeof value === "string";
  const nullableDate = (value: unknown) => value === null || typeof value === "string" && Number.isFinite(Date.parse(value));
  const id = (value: unknown) => typeof value === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
  return text(item.id) && text(item.number) && text(item.orderNumber) && typeof item.status === "string"
    && typeof item.requestedAt === "string" && Number.isFinite(Date.parse(item.requestedAt))
    && nullableDate(item.decidedAt) && nullableDate(item.receivedAt) && nullableDate(item.refundSettledAt)
    && (item.refundSettledAt === null || item.refund === "Refunded" || item.refund === "ClosedByHand")
    && typeof item.refundedAmount === "number" && Number.isFinite(item.refundedAmount) && item.refundedAmount >= 0
    && nullableText(item.reason) && nullableText(item.refund) && nullableText(item.refundProblem) && nullableText(item.refundNote)
    && Array.isArray(item.lines) && item.lines.every((line: unknown) => {
      if (typeof line !== "object" || line === null || Array.isArray(line)) return false;
      const row = line as Record<string, unknown>;
      return id(row.storeProductId) && id(row.variantId) && typeof row.productName === "string" && Number.isSafeInteger(row.quantity) && Number(row.quantity) > 0;
    });
}
const path = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/returns`;
const sameId = (actual: string, expected: string) => actual.toLowerCase() === expected.toLowerCase();

export async function getReturnDetails(storeId: string, id: string, signal?: AbortSignal): Promise<OrderReturn> {
  const value = await requestJson<unknown>(`${path(storeId)}/${encodeURIComponent(id)}`, { signal });
  if (!isReturn(value) || !sameId(value.id, id)) throw new HttpError("invalid-response", 200);
  return value;
}

export async function getReturns(storeId: string, signal?: AbortSignal, query: ReturnQuery = { status: "", refund: "" }): Promise<OrderReturn[]> {
  if ((query.status !== "" && !returnStatuses.includes(query.status)) || (query.refund !== "" && !refundStatuses.includes(query.refund))) throw new RangeError("Invalid return filter.");
  const parameters = new URLSearchParams();
  if (query.status) parameters.set("status", query.status);
  if (query.refund) parameters.set("refund", query.refund);
  const value = await requestJson<unknown>(`${path(storeId)}${parameters.size ? `?${parameters}` : ""}`, { signal });
  if (!Array.isArray(value) || value.length > returnLimit || !value.every(isReturn)
    || new Set(value.map((item) => item.id)).size !== value.length || new Set(value.map((item) => item.number)).size !== value.length
    || value.some((item) => (query.status && item.status !== query.status) || (query.refund && item.refund !== query.refund))) throw new HttpError("invalid-response", 200);
  return value;
}

export type ReturnDecision = "accept" | "refuse";
export async function decideReturn(storeId: string, id: string, decision: ReturnDecision) {
  const value = await requestJson<unknown>(`${path(storeId)}/${encodeURIComponent(id)}/${decision}`, { method: "POST" });
  if (!isReturn(value) || !sameId(value.id, id) || value.status !== (decision === "accept" ? "Accepted" : "Refused")) throw new HttpError("invalid-response", 200);
  return value;
}

export async function changeReturnMoney(storeId: string, id: string, action: "receive" | "retry" | "close", key: string, note?: string) {
  if (!key || key.length > 128) throw new RangeError("Invalid request key.");
  const operation = { receive: "receive", retry: "refund", close: "refund/close" }[action];
  const value = await requestJson<unknown>(`${path(storeId)}/${encodeURIComponent(id)}/${operation}`, {
    method: "POST", idempotencyKey: key, body: action === "close" ? { note } : undefined,
  });
  if (!isReturn(value) || !sameId(value.id, id) || value.status !== "Received" || value.refund === null
    || (action === "close" && value.refund !== "ClosedByHand")
    || (action === "retry" && value.refund !== "Refunded" && value.refund !== "ClosedByHand")) throw new HttpError("invalid-response", 200);
  return value;
}
