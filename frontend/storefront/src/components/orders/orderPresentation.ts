import type { Order } from "../../cart";

export type KnownOrderStatus =
  | "AwaitingPayment"
  | "Paid"
  | "Shipped"
  | "Cancelled"
  | "Refunded"
  | "unknown";

export type KnownDocumentKind = "Invoice" | "CreditNote" | "unknown";

export function knownOrderStatus(status: string): KnownOrderStatus {
  return [
    "AwaitingPayment",
    "Paid",
    "Shipped",
    "Cancelled",
    "Refunded",
  ].includes(status)
    ? (status as Exclude<KnownOrderStatus, "unknown">)
    : "unknown";
}

export function knownDocumentKind(kind: string): KnownDocumentKind {
  return kind === "Invoice" || kind === "CreditNote" ? kind : "unknown";
}

export function expectedDocumentKind(
  order: Order,
): Exclude<KnownDocumentKind, "unknown"> | null {
  if (order.status === "Paid" || order.status === "Shipped") return "Invoice";
  if (order.status === "Refunded") return "CreditNote";
  return null;
}

export function safeTrackingUrl(value: string | null) {
  if (!value) return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" || url.protocol === "http:"
      ? url.href
      : null;
  } catch {
    return null;
  }
}
