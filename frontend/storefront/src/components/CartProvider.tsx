import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { getCart, type Cart } from "../cart";
import { CartContext } from "../cartContext";

const adjustmentStorageKey = "shopforge.cart.adjusted";

type CartProviderState = {
  status: "loading" | "ready" | "error";
  cart: Cart | null;
  error: unknown | null;
  pending: boolean;
  adjusted: boolean;
};

export function CartProvider({ children }: { children: ReactNode }) {
  const [version, setVersion] = useState(0);
  const [state, setState] = useState<CartProviderState>(() => ({
    status: "loading",
    cart: null,
    error: null,
    pending: false,
    adjusted: readAdjustment(),
  }));
  const pending = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    getCart(controller.signal)
      .then((cart) => {
        if (controller.signal.aborted) return;
        rememberAdjustment(cart);
        setState((current) => ({
          status: "ready",
          cart,
          error: null,
          pending: false,
          adjusted: current.adjusted || cart.changed || readAdjustment(),
        }));
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted)
          setState((current) => ({
            ...current,
            status: "error",
            cart: null,
            error,
            pending: false,
          }));
      });
    return () => controller.abort();
  }, [version]);

  const apply = useCallback((cart: Cart) => {
    rememberAdjustment(cart);
    setState((current) => ({
      ...current,
      status: "ready",
      cart,
      error: null,
      adjusted: current.adjusted || cart.changed,
    }));
  }, []);
  const reload = useCallback(() => {
    setState((current) => ({
      ...current,
      status: "loading",
      cart: null,
      error: null,
      pending: false,
    }));
    setVersion((current) => current + 1);
  }, []);
  const acknowledgeAdjustment = useCallback(() => {
    writeAdjustment(false);
    setState((current) => ({ ...current, adjusted: false }));
  }, []);
  const mutate = useCallback(async (change: () => Promise<Cart>) => {
    if (pending.current) return null;
    pending.current = true;
    setState((current) => ({ ...current, pending: true, error: null }));
    try {
      const cart = await change();
      rememberAdjustment(cart);
      setState((current) => ({
        status: "ready",
        cart,
        error: null,
        pending: false,
        adjusted: current.adjusted || cart.changed,
      }));
      return cart;
    } catch (error) {
      setState((current) => ({ ...current, error, pending: false }));
      return null;
    } finally {
      pending.current = false;
    }
  }, []);

  return (
    <CartContext
      value={{
        ...state,
        acknowledgeAdjustment,
        apply,
        reload,
        mutate,
      }}
    >
      {children}
    </CartContext>
  );
}

function rememberAdjustment(cart: Cart) {
  if (cart.changed) writeAdjustment(true);
}

function readAdjustment() {
  if (typeof window === "undefined") return false;
  try {
    return window.sessionStorage.getItem(adjustmentStorageKey) === "true";
  } catch {
    return false;
  }
}

function writeAdjustment(value: boolean) {
  if (typeof window === "undefined") return;
  try {
    if (value) window.sessionStorage.setItem(adjustmentStorageKey, "true");
    else window.sessionStorage.removeItem(adjustmentStorageKey);
  } catch {
    return;
  }
}
