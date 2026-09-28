import { useOutletContext } from "react-router";
import type { AdminStore } from "./api";
import type { RequestState } from "./useRequest";

export type AdminOutletContext = {
  stores: RequestState<AdminStore[]>;
  reloadStores: () => void;
};

export type StoreOutletContext = {
  store: AdminStore;
  reloadStores: () => void;
};

export function useAdminOutlet() {
  return useOutletContext<AdminOutletContext>();
}

export function useSelectedStore() {
  return useOutletContext<StoreOutletContext>();
}
