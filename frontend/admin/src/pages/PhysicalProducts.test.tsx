// @vitest-environment jsdom
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type Product } from "../api";
import { HttpError } from "../api/http";
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
  lengthMm: 200, widthMm: 150, heightMm: 100, shipsAlone: false,
  brand: null,
  optionNames: [],
  variants: [{
    id: "variant-a",
    sku: "PHYSICAL-1",
    ean: "00012345600012",
    weightGrams: 250,
    lengthMm: 200, widthMm: 150, heightMm: 100, shipsAlone: false,
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
beforeEach(() => {
  vi.stubGlobal("CSS", { escape: (value: string) => value });
  HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  await i18n.changeLanguage("en");
});
afterAll(() => i18n.changeLanguage("en"));

describe("physical products", () => {
  it.each(["en", "cs"])("requires measurements, focuses errors and retains the package after a server refusal in %s", async (language) => {
    await i18n.changeLanguage(language);
    const user = userEvent.setup();
    const create = vi.spyOn(api, "createProduct").mockRejectedValue(new HttpError("http", 400, {
      errors: { lengthMm: ["Invalid length"], widthMm: ["Invalid width"], heightMm: ["Invalid height"] },
    }));
    render(<MemoryRouter><NewPhysicalProductPage /></MemoryRouter>);
    await user.type(screen.getByRole("textbox", { name: "SKU" }), "MEASURED");
    await user.click(screen.getByRole("button", { name: i18n.t("products:addProduct") }));
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("alert")));
    expect(create).not.toHaveBeenCalled();
    const fields = ["weightGrams", "lengthMm", "widthMm", "heightMm"] as const;
    for (const field of fields) {
      const input = screen.getByRole("textbox", { name: i18n.t(`products:${field}`) });
      expect(input).toHaveProperty("required", true);
      expect(input.getAttribute("aria-invalid")).toBe("true");
      await user.click(within(screen.getByRole("alert")).getByRole("button", {
        name: `${i18n.t(`products:fields.${field}`)}: ${i18n.t(field === "weightGrams" ? "products:validation.weightInvalid" : "products:validation.sizeInvalid")}`,
      }));
      expect(document.activeElement).toBe(input);
      await user.type(input, field === "weightGrams" ? "1" : "10000");
    }
    await user.click(screen.getByRole("button", { name: i18n.t("products:addProduct") }));
    const alert = await screen.findByRole("alert");
    const errors = within(alert).getAllByRole("button", { name: i18n.t("validation:numberInvalid") });
    expect(errors).toHaveLength(3);
    for (const [index, field] of ["lengthMm", "widthMm", "heightMm"].entries()) {
      await user.click(errors[index]);
      const input = document.querySelector(`[name="${field}"]`);
      expect(document.activeElement).toBe(input);
      expect(input).toHaveProperty("value", "10000");
    }
    expect(create).toHaveBeenCalledOnce();
    expect(create.mock.calls[0][0]).toMatchObject({ weightGrams: 1, lengthMm: 10000, widthMm: 10000, heightMm: 10000, shipsAlone: false });
  });

  it("validates SKU, EAN, integer grams, image type, and size", () => {
    expect(validateProduct({
      sku: "",
      ean: "1234567",
      weightGrams: "2.5",
      lengthMm: "200", widthMm: "150", heightMm: "100", shipsAlone: false,
      brand: "",
      partNumber: "",
      condition: "",
    }, true).map((issue) => issue.field)).toEqual(["sku", "ean", "weightGrams"]);
    expect(validateProduct({
      sku: "P",
      ean: "00012345600012",
      weightGrams: "1",
      lengthMm: "200", widthMm: "150", heightMm: "100", shipsAlone: false,
      brand: "Thonet",
      partNumber: "No-14",
      condition: "New",
    }, true)).toEqual([]);
    // A barcode whose check digit disagrees is the mistake the digit exists to catch.
    expect(validateProduct({
      sku: "P",
      ean: "00012345600011",
      weightGrams: "1",
      lengthMm: "200", widthMm: "150", heightMm: "100", shipsAlone: false,
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
    await user.type(screen.getByRole("textbox", { name: "Weight in grams" }), "250");
    await user.type(screen.getByRole("textbox", { name: "Length (mm)" }), "200");
    await user.type(screen.getByRole("textbox", { name: "Width (mm)" }), "150");
    await user.type(screen.getByRole("textbox", { name: "Height (mm)" }), "100");
    await user.click(screen.getByRole("checkbox", { name: "Ships alone" }));
    await user.click(screen.getByRole("button", { name: "Create product" }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect((screen.getByRole("textbox", { name: "EAN (optional)" }) as HTMLInputElement).value)
      .toBe("00012345600012");
    expect(screen.getByRole("textbox", { name: "Length (mm)" })).toHaveProperty("value", "200");
    expect(screen.getByRole("textbox", { name: "Width (mm)" })).toHaveProperty("value", "150");
    expect(screen.getByRole("textbox", { name: "Height (mm)" })).toHaveProperty("value", "100");
    expect(screen.getByRole("checkbox", { name: "Ships alone" })).toHaveProperty("checked", true);
    await user.click(screen.getByRole("button", { name: "Create product" }));
    expect(await screen.findByRole("heading", { name: "Created product detail" })).toBeTruthy();
    expect(create).toHaveBeenCalledTimes(2);
    expect(create.mock.calls[1][0]).toEqual({
      sku: "PHYSICAL-1",
      ean: "00012345600012",
      weightGrams: 250,
      lengthMm: 200, widthMm: 150, heightMm: 100, shipsAlone: true,
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
      id: "11111111-1111-1111-1111-111111111111", url: "/api/admin/products/product-a/images/11111111-1111-1111-1111-111111111111", altText: "Front view", position: 0,
    });
    const remove = vi.spyOn(api, "deleteProductImage").mockResolvedValue(undefined);
    vi.spyOn(api, "productImages").mockImplementation(async () => remove.mock.calls.length ? [] : [await upload.mock.results[0].value]);
    render(
      <ProductMediaSection productId="product-a" sku="PHYSICAL-1"
        images={[]} canManage refreshing={false} refreshFailed={false} reloadProducts={() => undefined} />,
    );

    expect(screen.getByText(/No images yet/)).toBeTruthy();
    await user.upload(
      screen.getByLabelText("Image file"),
      new File(["gif"], "wrong.gif", { type: "image/gif" }),
    );
    await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
    expect(upload).not.toHaveBeenCalled();
    expect(screen.getByText(/Choose a PNG, JPEG or WebP image/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Clear upload queue" }));

    const imageFile = new File(["png"], "front.png", { type: "image/png" });
    await user.upload(screen.getByLabelText("Image file"), imageFile);
    expect(createUrl).toHaveBeenCalledWith(imageFile);
    expect(screen.getByRole("img", { name: "Preview of front.png" })).toBeTruthy();
    await user.type(screen.getByRole("textbox", { name: "Description for front.png" }), "Front view");
    expect(upload).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
    await waitFor(() => expect(upload).toHaveBeenCalledWith("product-a", imageFile, "Front view"));
    expect(await screen.findByRole("img", { name: "Front view" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Clear upload queue" }));
    expect(revokeUrl).toHaveBeenCalledWith("blob:preview");

    await user.click(screen.getByRole("button", { name: "Remove Front view" }));
    expect(screen.getByRole("heading", { name: "Remove Front view?" })).toBeTruthy();
    expect(remove).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(remove).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Remove Front view" }));
    await user.click(within(screen.getByRole("form", { name: "Remove Front view?" })).getByRole("button", { name: "Remove image" }));
    await waitFor(() => expect(remove).toHaveBeenCalledWith("product-a", "11111111-1111-1111-1111-111111111111"));
    expect(await screen.findByText(/No images yet/)).toBeTruthy();
  });
});
