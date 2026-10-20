// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api, type ProductImage } from "../api";
import { HttpError } from "../api/http";
import { initializeI18n, i18n } from "../i18n";
import { ProductMediaSection } from "./ProductMediaSection";
import { validateProductImage } from "./productValidation";

const productId = "aaaaaaaa-1111-2222-3333-444444444444";
const ids = ["11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444"];
const image = (index: number, text = `View ${index + 1}`): ProductImage => ({ id: ids[index], url: `/api/admin/products/${productId}/images/${ids[index]}`, altText: text, position: index });
let inventory: ProductImage[];
const file = (name = "front.png") => new File(["png"], name, { type: "image/png" });
function mount(overrides = {}) {
  const props = { productId, sku: "CHAIR", images: inventory.map(image => ({ ...image })), canManage: true, refreshing: false, refreshFailed: false, reloadProducts: vi.fn(), ...overrides };
  return { ...render(<ProductMediaSection {...props} />), props };
}
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); inventory = [image(0), image(1)];
  vi.spyOn(URL, "createObjectURL").mockImplementation(() => `blob:${Math.random()}`);
  vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
  vi.spyOn(api, "productImages").mockImplementation(async () => inventory);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("submits the complete keyboard-arranged order and displays fresh saved positions", async () => {
  const user = userEvent.setup(); mount();
  const save = vi.spyOn(api, "reorderProductImages").mockImplementation(async (_product, order) => {
    inventory = order.map((id, position) => ({ ...inventory.find(image => image.id === id)!, position })); return inventory;
  });
  await user.click(screen.getByRole("button", { name: "Arrange photos" }));
  const form = screen.getByRole("form", { name: "Arrange photos" });
  const second = within(form).getAllByRole("listitem")[1];
  await user.click(within(second).getByRole("button", { name: "Make first" }));
  await waitFor(() => expect(document.activeElement?.textContent).toBe("1. View 2"));
  await user.click(within(form).getByRole("button", { name: "Save photo changes" }));
  expect(save).toHaveBeenCalledExactlyOnceWith(productId, [ids[1], ids[0]]);
  await screen.findByText("Photo changes saved.");
  expect(document.querySelector('.physical-image-grid img')?.getAttribute("src")).toBe(image(1).url);
});

it("edits and clears saved descriptions without changing order", async () => {
  const user = userEvent.setup(); mount();
  const save = vi.spyOn(api, "describeProductImage").mockImplementation(async (_product, id, text) => {
    inventory = inventory.map(image => image.id === id ? { ...image, altText: text.trim() || null } : image); return inventory[0];
  });
  await user.click(screen.getByRole("button", { name: "Edit description of photo 1" }));
  await user.clear(screen.getByRole("textbox", { name: "Image description" }));
  await user.click(screen.getByRole("button", { name: "Save photo changes" }));
  await screen.findByText("Photo changes saved."); expect(save).toHaveBeenCalledExactlyOnceWith(productId, ids[0], "");
  expect(inventory.map(image => image.id)).toEqual(ids.slice(0, 2));
  expect(screen.getByRole("img", { name: "Image 1 of CHAIR" })).toBeTruthy();
});

it("keeps a stale order draft until cancel and prevents sending obsolete IDs", async () => {
  const user = userEvent.setup(); const view = mount(); const save = vi.spyOn(api, "reorderProductImages");
  await user.click(screen.getByRole("button", { name: "Arrange photos" }));
  view.rerender(<ProductMediaSection {...view.props} images={[...inventory, image(2)]} />);
  await screen.findByText(/saved photos changed while/);
  expect(screen.getByRole("button", { name: "Save photo changes" })).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Cancel" })); expect(save).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Arrange photos" }));
  expect(within(screen.getByRole("form", { name: "Arrange photos" })).getAllByRole("listitem")).toHaveLength(3);
});

it("retains an alt draft after refusal and does not resend while an inventory refresh fails", async () => {
  const user = userEvent.setup(); mount();
  const save = vi.spyOn(api, "describeProductImage").mockRejectedValue(new HttpError("http", 400));
  vi.mocked(api.productImages).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(screen.getByRole("button", { name: "Edit description of photo 1" }));
  await user.type(screen.getByRole("textbox", { name: "Image description" }), " draft");
  await user.click(screen.getByRole("button", { name: "Save photo changes" }));
  await screen.findByText(/last.*|change was refused/i);
  expect(screen.getByRole("textbox", { name: "Image description" })).toHaveProperty("value", "View 1 draft");
  expect(screen.getByRole("button", { name: "Save photo changes" })).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Refresh photos" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "Save photo changes" })).toHaveProperty("disabled", false)); expect(save).toHaveBeenCalledOnce();
});

it.each([{ canManage: false }, { refreshing: true }, { refreshFailed: true }])("guards media writes for unavailable write scope %j", async scope => {
  mount(scope); const save = vi.spyOn(api, "reorderProductImages");
  const order = screen.queryByRole("button", { name: "Arrange photos" });
  expect(order === null || order.hasAttribute("disabled")).toBe(true); expect(save).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "Refresh photos" })).toHaveProperty("disabled", false);
});

it("requires a checked read before recovering from a malformed initial inventory", async () => {
  const user = userEvent.setup(); mount({ images: [{ ...image(0), url: "/foreign-image" }] });
  const order = vi.spyOn(api, "reorderProductImages").mockResolvedValue(inventory);
  expect(screen.getByLabelText("Image file")).toHaveProperty("disabled", true);
  expect(screen.queryByRole("button", { name: "Arrange photos" })).toBeNull();
  await user.click(screen.getByRole("button", { name: "Refresh photos" }));
  await user.click(await screen.findByRole("button", { name: "Arrange photos" }));
  await user.click(screen.getByRole("button", { name: "Save photo changes" }));
  await screen.findByText("Photo changes saved.");
  expect(order).toHaveBeenCalledExactlyOnceWith(productId, ids.slice(0, 2));
});

it("uploads a selected batch once, keeping per-file descriptions and true progress", async () => {
  const user = userEvent.setup(); mount();
  const upload = vi.spyOn(api, "uploadProductImage").mockImplementation(async (_product, _file, text) => {
    const saved = image(inventory.length, text); inventory = [...inventory, saved]; return saved;
  });
  await user.upload(screen.getByLabelText("Image file"), [file("one.png"), file("two.png")]);
  expect(upload).not.toHaveBeenCalled();
  await user.type(screen.getByRole("textbox", { name: "Description for one.png" }), "Front");
  await user.type(screen.getByRole("textbox", { name: "Description for two.png" }), "Back");
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  await screen.findByText("2 of 2 files confirmed uploaded.");
  expect(upload.mock.calls.map(call => [call[1].name, call[2]])).toEqual([["one.png", "Front"], ["two.png", "Back"]]);
  expect(api.productImages).toHaveBeenCalledTimes(2);
});

it("stops an uncertain upload, reads inventory and requires review before clearing or reselecting it", async () => {
  const user = userEvent.setup(); mount();
  const upload = vi.spyOn(api, "uploadProductImage").mockImplementationOnce(async () => { inventory = [...inventory, image(2)]; throw new HttpError("network", null); })
    .mockImplementationOnce(async () => { const saved = image(3); inventory = [...inventory, saved]; return saved; });
  await user.upload(screen.getByLabelText("Image file"), [file("one.png"), file("two.png")]);
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  await screen.findByText("Outcome unknown · review photos"); expect(upload).toHaveBeenCalledOnce();
  expect(screen.getByRole("button", { name: "Clear upload queue" })).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  await screen.findByText("1 of 2 files confirmed uploaded.");
  expect(upload.mock.calls.map(call => call[1].name)).toEqual(["one.png", "two.png"]);
  await user.click(screen.getByRole("checkbox", { name: /I reviewed the current photos/ }));
  await user.click(screen.getByRole("button", { name: "Clear upload queue" }));
  expect(upload).toHaveBeenCalledTimes(2); expect(screen.getByLabelText("Image file")).toHaveProperty("disabled", false);
  await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText("Image file")));
});

it("keeps a confirmed upload when its read fails and only continues unsent files after recovery", async () => {
  const user = userEvent.setup(); mount();
  const upload = vi.spyOn(api, "uploadProductImage").mockImplementation(async () => { const saved = image(inventory.length); inventory = [...inventory, saved]; return saved; });
  vi.mocked(api.productImages).mockRejectedValueOnce(new HttpError("http", 503));
  await user.upload(screen.getByLabelText("Image file"), [file("one.png"), file("two.png")]);
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  await screen.findByText("1 of 2 files confirmed uploaded."); expect(upload).toHaveBeenCalledOnce();
  expect(screen.getByRole("button", { name: "Upload remaining files" })).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Refresh photos" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "Upload remaining files" })).toHaveProperty("disabled", false));
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  await screen.findByText("2 of 2 files confirmed uploaded."); expect(upload.mock.calls.map(call => call[1].name)).toEqual(["one.png", "two.png"]);
});

it("treats an upload answer reusing a saved image ID as uncertain and never sends the next file", async () => {
  const user = userEvent.setup(); mount();
  const upload = vi.spyOn(api, "uploadProductImage").mockResolvedValue(inventory[0]);
  await user.upload(screen.getByLabelText("Image file"), [file("one.png"), file("two.png")]);
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  await screen.findByText("Outcome unknown · review photos");
  expect(upload).toHaveBeenCalledOnce(); expect(api.productImages).toHaveBeenCalledOnce();
  expect(screen.getByRole("button", { name: "Clear upload queue" })).toHaveProperty("disabled", true);
});

it("invalidates an uncertain-upload review when the saved inventory changes", async () => {
  const user = userEvent.setup(); const view = mount();
  vi.spyOn(api, "uploadProductImage").mockRejectedValue(new HttpError("network", null));
  await user.upload(screen.getByLabelText("Image file"), file());
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  await screen.findByText("Outcome unknown · review photos");
  await user.click(screen.getByRole("checkbox", { name: /I reviewed the current photos/ }));
  expect(screen.getByRole("button", { name: "Clear upload queue" })).toHaveProperty("disabled", false);
  view.rerender(<ProductMediaSection {...view.props} images={[...inventory, image(2)]} />);
  expect(screen.getByRole("button", { name: "Clear upload queue" })).toHaveProperty("disabled", true);
  expect(screen.getByRole("checkbox", { name: /I reviewed the current photos/ })).toHaveProperty("checked", false);
});

it.each(["stop", "leave", "role"])("does not send remaining files after %s", async kind => {
  const user = userEvent.setup(); const view = mount(); let release!: (value: ProductImage) => void;
  const upload = vi.spyOn(api, "uploadProductImage").mockReturnValue(new Promise(resolve => { release = resolve; }));
  await user.upload(screen.getByLabelText("Image file"), [file("one.png"), file("two.png")]);
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  if (kind === "stop") await user.click(screen.getByRole("button", { name: "Stop after current file" }));
  else if (kind === "leave") view.unmount();
  else view.rerender(<ProductMediaSection {...view.props} canManage={false} />);
  inventory = [...inventory, image(2)]; await act(async () => release(image(2)));
  expect(upload).toHaveBeenCalledOnce();
  if (kind === "stop") expect(await screen.findByText("1 of 2 files confirmed uploaded.")).toBeTruthy();
});

it("shows 429 guidance without resending and keeps later files unsent", async () => {
  const user = userEvent.setup(); mount();
  const upload = vi.spyOn(api, "uploadProductImage").mockRejectedValue(new HttpError("http", 429, null, null, "25"));
  await user.upload(screen.getByLabelText("Image file"), [file("one.png"), file("two.png")]);
  await user.click(screen.getByRole("button", { name: "Upload remaining files" }));
  expect(await screen.findByText(/Wait at least 25 seconds/)).toBeTruthy(); expect(upload).toHaveBeenCalledOnce();
  expect(screen.getByRole("textbox", { name: "Description for two.png" })).toHaveProperty("readOnly", false);
});

it("bounds selections and validates the 200-character limit while allowing explicit empty descriptions", async () => {
  const user = userEvent.setup(); mount(); await user.upload(screen.getByLabelText("Image file"), Array.from({ length: 11 }, (_, index) => file(`${index}.png`)));
  expect(screen.getByText("Choose no more than 10 files at a time.")).toBeTruthy(); expect(URL.createObjectURL).not.toHaveBeenCalled();
  expect(validateProductImage(file(), "")).toEqual([]);
  expect(validateProductImage(file(), "a".repeat(201))).toEqual([{ field: "altText", key: "altTextInvalid" }]);
});

it.each(["en", "cs"])("localizes the media workflow in %s without inline styles", async language => {
  await i18n.changeLanguage(language); mount();
  expect(screen.getByRole("button", { name: i18n.t("products:mediaOrder") })).toBeTruthy();
  expect(screen.getByText(i18n.t("products:mediaFirstHint"))).toBeTruthy();
  expect(document.querySelectorAll("style, [style], script")).toHaveLength(0);
});
