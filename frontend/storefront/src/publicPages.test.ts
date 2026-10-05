import { expect, it } from "vitest";
import { categoryPath, productPath, publicRoutes } from "./publicPages";

// These exact shapes are what the backend publishes. `StoreUrlTests` on the server asserts the same two, so
// moving one side alone fails there as well as here.
it("serves the page addresses the backend publishes", () => {
  expect(publicRoutes).toEqual({ product: "p/:slug", category: "c/:slug" });
  expect(productPath("oak-chair")).toBe("/p/oak-chair");
  expect(categoryPath("chairs")).toBe("/c/chairs");
});

// A slug is somebody's typing and ends up in an address.
it("escapes a slug that needs it", () => {
  expect(productPath("a/b c")).toBe("/p/a%2Fb%20c");
});
