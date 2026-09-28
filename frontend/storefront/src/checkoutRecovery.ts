import type { PlacedOrder } from "./cart";

const recoveryKey = "shopforge.checkout.recovery";

export type CheckoutRecovery = {
  orderPath: string;
};

export function orderPath(order: Pick<PlacedOrder, "number" | "token">) {
  return (
    "/order/" +
    encodeURIComponent(order.number) +
    "?token=" +
    encodeURIComponent(order.token)
  );
}

export function rememberCheckout(order: PlacedOrder) {
  const recovery = { orderPath: orderPath(order) };
  if (typeof window !== "undefined") {
    try {
      window.sessionStorage.setItem(recoveryKey, JSON.stringify(recovery));
    } catch {
      // A blocked storage API must not prevent an already placed order from continuing.
    }
  }
  return recovery.orderPath;
}

export function readCheckoutRecovery(): CheckoutRecovery | null {
  if (typeof window === "undefined") return null;
  try {
    const value = JSON.parse(
      window.sessionStorage.getItem(recoveryKey) ?? "null",
    ) as unknown;
    if (
      value &&
      typeof value === "object" &&
      "orderPath" in value &&
      typeof value.orderPath === "string" &&
      value.orderPath.startsWith("/order/")
    ) {
      return { orderPath: value.orderPath };
    }
  } catch {
    return null;
  }
  return null;
}

export function clearCheckoutRecovery() {
  if (typeof window === "undefined") return;
  try {
    window.sessionStorage.removeItem(recoveryKey);
  } catch {
    return;
  }
}
