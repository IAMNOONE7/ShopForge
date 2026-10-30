import { requestJson } from "./api/http";
import { isFreeShippingThreshold } from "./freeShipping";

export type StoreTheme = {
  primaryColor: string;
  secondaryColor: string;
  borderRadius: number;
};

export type Store = {
  id: string;
  name: string;
  currency: string;
  culture: string;
  logoUrl: string | null;
  freeShippingFrom?: number | null;
  theme: StoreTheme;
  // Keys a provider issued to be used in this page: a carrier's map will not open without one.
  providerKeys: { provider: string; key: string }[];
};

export function fetchStore(signal: AbortSignal) {
  return requestJson<Store>("/api/storefront/store", {
    signal,
    notifyUnauthorized: false,
  }).then(store => ({ ...store, freeShippingFrom: isFreeShippingThreshold(store.freeShippingFrom) ? store.freeShippingFrom : null }));
}

export function applyStore(store: Store) {
  const root = document.documentElement;

  root.style.setProperty("--color-primary", store.theme.primaryColor);
  root.style.setProperty("--color-secondary", store.theme.secondaryColor);
  root.style.setProperty("--radius", `${store.theme.borderRadius}px`);
  document.title = store.name;
}
