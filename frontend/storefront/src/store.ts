import { requestJson } from "./api/http";

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
  theme: StoreTheme;
};

export function fetchStore(signal: AbortSignal) {
  return requestJson<Store>("/api/storefront/store", {
    signal,
    notifyUnauthorized: false,
  });
}

export function applyStore(store: Store) {
  const root = document.documentElement;

  root.style.setProperty("--color-primary", store.theme.primaryColor);
  root.style.setProperty("--color-secondary", store.theme.secondaryColor);
  root.style.setProperty("--radius", `${store.theme.borderRadius}px`);
  document.title = store.name;
}
