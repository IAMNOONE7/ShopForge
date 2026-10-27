import { readSpreadsheet } from "./spreadsheet";

export type StoreExportKind = "catalog" | "orders";
export const storeExportsApi = {
  read: (storeId: string, kind: StoreExportKind, signal?: AbortSignal) => readSpreadsheet(
    "/api/admin/stores/" + encodeURIComponent(storeId) + (kind === "catalog" ? "/export" : "/orders/export"),
    kind === "catalog" ? "shopforge-catalogue.xlsx" : "shopforge-orders.xlsx",
    signal,
  ),
};
