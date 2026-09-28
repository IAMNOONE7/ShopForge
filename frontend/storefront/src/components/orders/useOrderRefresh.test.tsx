// @vitest-environment jsdom
import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Order } from "../../cart";
import { orderNeedsRefresh, useOrderRefresh } from "./useOrderRefresh";

function makeOrder(
  status: string,
  documents: Order["documents"] = [],
): Order {
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

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((yes) => {
    resolve = yes;
  });
  return { promise, resolve };
}

async function advance(milliseconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });
}

afterEach(() => {
  vi.useRealTimers();
  Object.defineProperty(document, "visibilityState", {
    configurable: true,
    value: "visible",
  });
});

describe("useOrderRefresh", () => {
  it("retains the current receipt while polling and stops when the invoice arrives", async () => {
    vi.useFakeTimers();
    const refresh = deferred<Order>();
    const invoice = {
      number: "INV-1",
      kind: "Invoice",
      issuedAt: "2026-09-26T10:05:00Z",
    };
    const load = vi
      .fn<(signal: AbortSignal) => Promise<Order>>()
      .mockResolvedValueOnce(makeOrder("AwaitingPayment"))
      .mockImplementationOnce(() => refresh.promise)
      .mockResolvedValueOnce(makeOrder("Paid", [invoice]));
    const { result } = renderHook(() => useOrderRefresh("order:1", load));

    await act(async () => undefined);
    expect(result.current.request.status).toBe("ready");

    await advance(3_000);
    expect(load).toHaveBeenCalledTimes(2);
    expect(
      result.current.request.status === "ready" &&
        result.current.request.data.status,
    ).toBe("AwaitingPayment");
    expect(
      result.current.request.status === "ready" &&
        result.current.request.refreshing,
    ).toBe(true);

    await act(async () => {
      refresh.resolve(makeOrder("Paid"));
      await refresh.promise;
    });
    expect(result.current.waiting).toBe(true);
    expect(
      result.current.request.status === "ready" &&
        result.current.request.data.status,
    ).toBe("Paid");

    await advance(3_000);
    expect(load).toHaveBeenCalledTimes(3);
    expect(result.current.waiting).toBe(false);
    expect(
      result.current.request.status === "ready" &&
        result.current.request.data.documents,
    ).toEqual([invoice]);

    await advance(60_000);
    expect(load).toHaveBeenCalledTimes(3);
  });

  it("pauses polling in a hidden tab and aborts the request on unmount", async () => {
    vi.useFakeTimers();
    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "hidden",
    });
    const signals: AbortSignal[] = [];
    const load = vi.fn((signal: AbortSignal) => {
      signals.push(signal);
      return Promise.resolve(makeOrder("AwaitingPayment"));
    });
    const { result, unmount } = renderHook(() =>
      useOrderRefresh("order:1", load),
    );

    await act(async () => undefined);
    expect(result.current.request.status).toBe("ready");
    await advance(30_000);
    expect(load).toHaveBeenCalledTimes(1);

    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "visible",
    });
    act(() => document.dispatchEvent(new Event("visibilitychange")));
    await advance(3_000);
    expect(load).toHaveBeenCalledTimes(2);

    unmount();
    expect(signals.at(-1)?.aborted).toBe(true);
    await advance(30_000);
    expect(load).toHaveBeenCalledTimes(2);
  });

  it("ends automatic checks after two active minutes and manual refresh restarts them", async () => {
    vi.useFakeTimers();
    const load = vi
      .fn<(signal: AbortSignal) => Promise<Order>>()
      .mockResolvedValue(makeOrder("AwaitingPayment"));
    const { result } = renderHook(() => useOrderRefresh("order:1", load));

    await act(async () => undefined);
    for (let index = 0; index < 3; index += 1) await advance(3_000);
    for (let index = 0; index < 11; index += 1) await advance(10_000);
    await advance(1_000);

    expect(result.current.timedOut).toBe(true);
    const callsAtTimeout = load.mock.calls.length;
    await advance(300_000);
    expect(load).toHaveBeenCalledTimes(callsAtTimeout);

    act(() => result.current.refresh());
    await act(async () => undefined);
    expect(result.current.timedOut).toBe(false);
    expect(load).toHaveBeenCalledTimes(callsAtTimeout + 1);
  });
});

describe("orderNeedsRefresh", () => {
  it("only polls states with a pending status or expected document", () => {
    expect(orderNeedsRefresh(makeOrder("AwaitingPayment"))).toBe(true);
    expect(orderNeedsRefresh(makeOrder("Paid"))).toBe(true);
    expect(orderNeedsRefresh(makeOrder("Shipped"))).toBe(true);
    expect(orderNeedsRefresh(makeOrder("Refunded"))).toBe(true);
    expect(orderNeedsRefresh(makeOrder("Cancelled"))).toBe(false);
    expect(
      orderNeedsRefresh(
        makeOrder("Paid", [
          { number: "INV-1", kind: "Invoice", issuedAt: "2026-09-26T10:05:00Z" },
        ]),
      ),
    ).toBe(false);
  });
});
