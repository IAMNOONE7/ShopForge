// @vitest-environment jsdom
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { api, type Product } from "../api";
import { ProductMediaSection } from "../components/ProductMediaSection";
import { validateProduct, validateProductImage } from "../components/productValidation";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { NewPhysicalProductPage } from "./NewPhysicalProductPage";
import { ProductsPage } from "./ProductsPage";

const product: Product = {
  id: "product-a",
  sku: "PHYSICAL-1",
  ean: "00012345600012",
  weightGrams: 250,
  brand: null,
  optionNames: [],
  variants: [{
    id: "variant-a",
    sku: "PHYSICAL-1",
    ean: "00012345600012",
    weightGrams: 250,
    partNumber: null,
    condition: null,
    optionValues: [],
    position: 0,
  }],
  images: [],
};

function withSession(children: React.ReactNode, role = "Owner") {
  return (
    <SessionContext.Provider value={{
      user: { id: "user-a", email: "owner@example.test", role, tenantId: "tenant-a" },
      logout: async () => undefined,
      logoutPending: false,
      logoutError: null,
    }}>
      {children}
    </SessionContext.Provider>
  );
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});
afterAll(() => i18n.changeLanguage("en"));

describe("physical products", () => {
  it("validates SKU, EAN, integer grams, image type, and size", () => {
    expect(validateProduct({
      sku: "",
      ean: "1234567",
      weightGrams: "2.5",
      brand: "",
      partNumber: "",
      condition: "",
    }, true).map((issue) => issue.field)).toEqual(["sku", "ean", "weightGrams"]);
    expect(validateProduct({
      sku: "P",
      ean: "00012345600012",
      weightGrams: "0",
      brand: "Thonet",
      partNumber: "No-14",
      condition: "New",
    }, true)).toEqual([]);
    // A barcode whose check digit disagrees is the mistake the digit exists to catch.
    expect(validateProduct({
      sku: "P",
      ean: "00012345600011",
      weightGrams: "0",
      brand: "",
      partNumber: "",
      condition: "",
    }, true).map((issue) => issue.field)).toEqual(["ean"]);
    expect(validateProductImage(
      new File(["gif"], "wrong.gif", { type: "image/gif" }),
      "description",
    ).map((issue) => issue.key)).toEqual(["fileTypeInvalid"]);
    expect(validateProductImage(
      new File([new Uint8Array(5 * 1024 * 1024 + 1)], "huge.png", { type: "image/png" }),
      "description",
    ).map((issue) => issue.key)).toEqual(["fileSizeInvalid"]);
  });

  it("keeps read failures distinct from empty lists and permits retry", async () => {
    const user = userEvent.setup();
    const products = vi.spyOn(api, "products")
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValue([]);
    render(withSession(
      <MemoryRouter>
        <ProductsPage />
      </MemoryRouter>,
      "OrderManager",
    ));

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText("No physical products yet")).toBeNull();
    expect(screen.queryByRole("link", { name: "New physical product" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("No physical products yet")).toBeTruthy();
    expect(products).toHaveBeenCalledTimes(2);
  });

  it("preserves create values after a conflict and navigates on retry", async () => {
    const user = userEvent.setup();
    const create = vi.spyOn(api, "createProduct")
      .mockRejectedValueOnce(new Error("duplicate"))
      .mockResolvedValue(product);
    render(
      <MemoryRouter initialEntries={["/products/new"]}>
        <Routes>
          <Route path="/products/new" element={<NewPhysicalProductPage />} />
          <Route path="/products/:productId" element={<h1>Created product detail</h1>} />
        </Routes>
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("button", { name: "Create product" }));
    expect(create).not.toHaveBeenCalled();
    await user.type(screen.getByRole("textbox", { name: "SKU" }), "PHYSICAL-1");
    await user.type(screen.getByRole("textbox", { name: "EAN (optional)" }), "00012345600012");
    await user.type(screen.getByRole("textbox", { name: "Weight in grams (optional)" }), "250");
    await user.click(screen.getByRole("button", { name: "Create product" }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect((screen.getByRole("textbox", { name: "EAN (optional)" }) as HTMLInputElement).value)
      .toBe("00012345600012");
    await user.click(screen.getByRole("button", { name: "Create product" }));
    expect(await screen.findByRole("heading", { name: "Created product detail" })).toBeTruthy();
    expect(create).toHaveBeenCalledTimes(2);
    expect(create.mock.calls[1][0]).toEqual({
      sku: "PHYSICAL-1",
      ean: "00012345600012",
      weightGrams: 250,
      brand: null,
      partNumber: null,
      condition: null,
    });
  });

  it("uploads explicitly, then removes the named image only after confirmation", async () => {
    const user = userEvent.setup({ applyAccept: false });
    const createUrl = vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:preview");
    const revokeUrl = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
    const upload = vi.spyOn(api, "uploadProductImage").mockResolvedValue({
      id: "image-a", url: "/image-a", altText: "Front view", position: 0,
    });
    const remove = vi.spyOn(api, "deleteProductImage").mockResolvedValue(undefined);
    render(
      <ProductMediaSection productId="product-a" sku="PHYSICAL-1"
        images={[]} reloadProducts={() => undefined} />,
    );

    expect(screen.getByText(/No images yet/)).toBeTruthy();
    await user.upload(
      screen.getByLabelText("Image file"),
      new File(["gif"], "wrong.gif", { type: "image/gif" }),
    );
    await user.type(screen.getByRole("textbox", { name: "Image description" }), "Front view");
    await user.click(screen.getByRole("button", { name: "Upload image" }));
    expect(upload).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: /Choose a PNG, JPEG or WebP image/ })).toBeTruthy();

    const imageFile = new File(["png"], "front.png", { type: "image/png" });
    await user.upload(screen.getByLabelText("Image file"), imageFile);
    expect(createUrl).toHaveBeenCalledWith(imageFile);
    expect(screen.getByRole("img", { name: "Selected image preview" })).toBeTruthy();
    expect(upload).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Upload image" }));
    await waitFor(() => expect(upload).toHaveBeenCalledWith("product-a", imageFile, "Front view"));
    expect(await screen.findByRole("img", { name: "Front view" })).toBeTruthy();
    expect(revokeUrl).toHaveBeenCalledWith("blob:preview");

    await user.click(screen.getByRole("button", { name: "Remove Front view" }));
    expect(within(screen.getByRole("group")).getByText("Remove Front view?")).toBeTruthy();
    expect(remove).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(remove).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Remove Front view" }));
    await user.click(within(screen.getByRole("group")).getByRole("button", { name: "Remove image" }));
    await waitFor(() => expect(remove).toHaveBeenCalledWith("product-a", "image-a"));
    expect(await screen.findByText(/No images yet/)).toBeTruthy();
  });
});
