import { createContext, useContext } from "react";
import type { Store } from "./store";
import { formatCurrency } from "./utils/format";

export const StoreContext = createContext<Store | null>(null);

export function useStore(): Store {
  const store = useContext(StoreContext);

  if (!store) {
    throw new Error("useStore must be used inside StoreContext.");
  }

  return store;
}

export function formatPrice(price: number, store: Store) {
  return formatCurrency(price, store.culture, store.currency);
}
