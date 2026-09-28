import { describe, expect, it } from "vitest";
import { adminHomePath, safeAdminReturnPath } from "./routing";
import { canManageCatalog, canManageStore, knownRole } from "./session";

describe("admin routing and policies", () => {
  it("keeps only same-app return paths for root and /admin builds", () => {
    expect(safeAdminReturnPath("/stores/one?tab=x", "/")).toBe(
      "/stores/one?tab=x",
    );
    expect(safeAdminReturnPath("//outside.example", "/")).toBe("/products");
    expect(safeAdminReturnPath("https://outside.example", "/")).toBe(
      "/products",
    );
    expect(safeAdminReturnPath("/admin/stores/one", "/admin/")).toBe(
      "/admin/stores/one",
    );
    expect(safeAdminReturnPath("/products", "/admin/")).toBe("/admin/products");
    expect(adminHomePath("/admin/")).toBe("/admin/products");
  });

  it("matches the server's catalog and store management role policies", () => {
    expect(canManageStore("Owner")).toBe(true);
    expect(canManageStore("Admin")).toBe(true);
    expect(canManageStore("CatalogManager")).toBe(false);
    expect(canManageCatalog("Owner")).toBe(true);
    expect(canManageCatalog("Admin")).toBe(true);
    expect(canManageCatalog("CatalogManager")).toBe(true);
    for (const role of ["OrderManager", "Warehouse", "Support", "unknown"])
      expect(canManageCatalog(role)).toBe(false);
    expect(knownRole("not-a-role")).toBe("unknown");
  });
});
