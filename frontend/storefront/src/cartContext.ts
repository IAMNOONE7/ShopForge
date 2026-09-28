import { createContext, useContext } from "react";
import type { Cart } from "./cart";

export type CartState = {
  status: "loading" | "ready" | "error";
  cart: Cart | null;
  error: unknown | null;
  pending: boolean;
  adjusted: boolean;
  acknowledgeAdjustment: () => void;
  apply: (cart: Cart) => void;
  reload: () => void;
  mutate: (change: () => Promise<Cart>) => Promise<Cart | null>;
};

export const CartContext = createContext<CartState | null>(null);

export function useCart(): CartState {
  const state = useContext(CartContext);

  if (!state) {
    throw new Error("useCart must be used inside CartContext.");
  }

  return state;
}
