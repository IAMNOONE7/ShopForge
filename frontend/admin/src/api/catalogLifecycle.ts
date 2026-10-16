import { HttpError, requestJson } from "./http";

export type CatalogKind = "products" | "categories";
export type CatalogAction = "archive" | "restore" | "delete";

export async function changeCatalogLifecycle(storeId: string, kind: CatalogKind, id: string, action: CatalogAction) {
  const path = `/api/admin/stores/${encodeURIComponent(storeId)}/${kind}/${encodeURIComponent(id)}`;
  const result = await requestJson<unknown>(action === "delete" ? path : `${path}/${action}`, {
    method: action === "delete" ? "DELETE" : "POST",
  });
  if (result !== undefined) throw new HttpError("invalid-response", null);
}
