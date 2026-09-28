import { describe, expect, it } from "vitest";
import type { Order } from "../../cart";
import {
  expectedDocumentKind,
  knownDocumentKind,
  knownOrderStatus,
  safeTrackingUrl,
} from "./orderPresentation";

function order(status: string, documents: Order["documents"] = []): Order {
  return {
    number: "2026-1",
    placedAt: "2026-09-26T10:00:00Z",
    status,
    email: "customer@example.test",
    currency: "EUR",
    paymentMethod: "Bank transfer",
    shippingMethod: "Courier",
    shippingPrice: 10,
    itemsTotal: 110,
    vatTotal: 20,
    grandTotal: 120,
    discount: null,
    pickupPoint: null,
    shipment: null,
    documents,
    lines: [],
  };
}

describe("order presentation", () => {
  it("only exposes absolute HTTP tracking links", () => {
    expect(safeTrackingUrl("https://carrier.example/track/123")).toBe(
      "https://carrier.example/track/123",
    );
    expect(safeTrackingUrl("http://carrier.example/track/123")).toBe(
      "http://carrier.example/track/123",
    );
    expect(safeTrackingUrl("javascript:alert(1)")).toBeNull();
    expect(safeTrackingUrl("/track/123")).toBeNull();
    expect(safeTrackingUrl("not a URL")).toBeNull();
    expect(safeTrackingUrl(null)).toBeNull();
  });

  it("falls back safely for future statuses and document kinds", () => {
    expect(knownOrderStatus("Processing")).toBe("unknown");
    expect(knownDocumentKind("PackingSlip")).toBe("unknown");
  });

  it("identifies documents that are expected after settled states", () => {
    expect(expectedDocumentKind(order("Paid"))).toBe("Invoice");
    expect(expectedDocumentKind(order("Shipped"))).toBe("Invoice");
    expect(expectedDocumentKind(order("Refunded"))).toBe("CreditNote");
    expect(expectedDocumentKind(order("Cancelled"))).toBeNull();
    expect(
      expectedDocumentKind(
        order("Paid", [
          { number: "INV-1", kind: "Invoice", issuedAt: "2026-09-26T10:05:00Z" },
        ]),
      ),
    ).toBe("Invoice");
  });
});
