// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes, useLocation } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import { normalizedStoreAddress, storeAddressIssue, validateCreateStore, type CreateStoreDraft } from "../components/storeValidation";
import { i18n, initializeI18n } from "../i18n";
import { NewStorePage } from "./NewStorePage";

const created: AdminStore = { id: "returned-id", name: "Wooden Home", currency: "CZK", culture: "cs-CZ", status: "draft",
  primaryHostName: "wooden-home.shops.example.cz", logoUrl: null, company: null, returnWindowDays: 14,
  theme: { primaryColor: "#1F6FEB", secondaryColor: "#EEF4FF", borderRadius: 6 } };
const valid: CreateStoreDraft = { name: "Wooden Home", hostName: "wooden-home", currency: "CZK", culture: "cs-CZ",
  primaryColor: "#1F6FEB", secondaryColor: "#EEF4FF", borderRadius: "6" };
const taken = new HttpError("http", 409, { title: "The host name is already used by another store" });
const unavailable = new HttpError("http", 400, { errors: {
  hostName: ["The platform has no host name suffix configured, so it cannot give a store an address."],
} });

function Location() { return <output>{useLocation().pathname}</output>; }
function route() {
  const reload = vi.fn();
  render(<MemoryRouter initialEntries={["/stores/new"]}>
    <Link to="/stores">Leave creation</Link><Location />
    <Routes><Route element={<Outlet context={{ reloadStores: reload }} />}>
      <Route path="/stores/new" element={<NewStorePage />} />
      <Route path="/stores" element={<h1>Stores destination</h1>} />
      <Route path="/stores/:storeId/settings" element={<h1>Settings destination</h1>} />
    </Route></Routes>
  </MemoryRouter>);
  return reload;
}
async function fill(user: ReturnType<typeof userEvent.setup>, hostName = "wooden-home") {
  await user.type(screen.getByRole("textbox", { name: "Name" }), "Wooden Home");
  await user.type(screen.getByRole("textbox", { name: "Address name" }), hostName);
}

beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  vi.stubGlobal("CSS", { escape: (value: string) => value });
  HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => {
  cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals();
  window.history.replaceState(null, "", "/");
  await i18n.changeLanguage("en");
});

describe("store address syntax", () => {
  it.each(["x", "123", "wooden-home", " WOODEN-HOME. ", "a".repeat(63), "wooden-home.shops.example.cz", "wooden-home.localhost", "wooden-home.xn--bcher-kva.example"])("accepts label or full-address syntax for %s", (hostName) => {
    expect(validateCreateStore({ ...valid, hostName })).toEqual([]);
  });

  it.each(["", "a".repeat(64), "wooden--home", "-wooden-home", "wooden-home-", "wooden_home", "dřevo", "wooden home", "https://wooden-home.example.cz", "wooden-home:5173", "wooden-home/path", "a..example.cz", ".wooden-home"])("rejects invalid address syntax for %s", (hostName) => {
    expect(validateCreateStore({ ...valid, hostName }).map((issue) => issue.field)).toEqual(["hostName"]);
  });

  it("normalizes case, surrounding space and trailing root dots without composing a suffix", () => {
    expect(normalizedStoreAddress(" Wooden-Home... ")).toBe("wooden-home");
    expect(normalizedStoreAddress(" Wooden-Home.Shops.Example.CZ. ")).toBe("wooden-home.shops.example.cz");
  });

  it("does not treat every conflict as a taken address", () => {
    expect(storeAddressIssue(new HttpError("http", 409, { title: "This plan allows no more stores" }))).toBeNull();
    expect(storeAddressIssue(new HttpError("http", 403))).toBeNull();
  });
});

describe("new store form", () => {
  it.each(["en", "cs"])("keeps the draft and offers plan review after a store-cap refusal in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); const create = vi.spyOn(api, "createStore").mockRejectedValue(new HttpError("http", 409, { title: "This plan allows no more stores", detail: "untranslated-private-diagnostic" })); route();
    fireEvent.change(screen.getByRole("textbox", { name: i18n.t("stores:name") }), { target: { value: "Wooden Home" } }); fireEvent.change(screen.getByRole("textbox", { name: i18n.t("stores:addressName") }), { target: { value: "wooden-home" } });
    await user.click(screen.getByRole("button", { name: i18n.t("stores:create") })); await screen.findByText(i18n.t("planUsage:storesRefused"));
    expect(screen.getByRole("textbox", { name: i18n.t("stores:addressName") })).toHaveProperty("value", "wooden-home"); expect(screen.getByRole("textbox", { name: i18n.t("stores:name") })).toHaveProperty("value", "Wooden Home"); expect(screen.getByRole("alert").textContent).not.toContain("untranslated-private"); expect(screen.getAllByRole("link", { name: i18n.t("planUsage:reviewPlan") }).every(link => link.getAttribute("href") === "/plan")).toBe(true); expect(document.activeElement).toBe(screen.getByRole("alert")); expect(create).toHaveBeenCalledTimes(1);
  });
  it.each(["en", "cs"])("explains platform addresses and uses Czech launch defaults in %s", async (language) => {
    await i18n.changeLanguage(language); route();
    const address = screen.getByRole("textbox", { name: language === "en" ? "Address name" : "Název v adrese" });
    expect(address.getAttribute("placeholder")).toBe("wooden-home");
    expect(address.getAttribute("aria-describedby")).toContain("hostName-hint");
    expect(document.getElementById("hostName-hint")?.textContent).toContain(language === "en" ? "Add and verify your own domain" : "Vlastní doménu přidáte a ověříte");
    expect(screen.getByRole("textbox", { name: language === "en" ? "Currency" : "Měna" })).toHaveProperty("value", "CZK");
    expect(screen.getByRole("textbox", { name: language === "en" ? "Currency" : "Měna" })).toHaveProperty("readOnly", true);
    expect(screen.getByRole("textbox", { name: language === "en" ? "Formatting culture" : "Kultura formátování" })).toHaveProperty("value", "cs-CZ");
    expect(screen.getByText(language === "en" ? "CZK 49.00" : "49,00 Kč")).toBeTruthy();
    expect(document.body.textContent).not.toContain("€");
    expect(document.body.textContent).not.toContain("shops.example.cz");
  });

  it.each([
    { address: " WOODEN-HOME. ", expected: "wooden-home" },
    { address: " Wooden-Home.Shops.Example.CZ. ", expected: "wooden-home.shops.example.cz" },
  ])("posts $address under the existing hostName key and opens the returned ID", async ({ address, expected }) => {
    const user = userEvent.setup();
    const fetch = vi.fn().mockResolvedValue(Response.json(created, { status: 201 }));
    vi.stubGlobal("fetch", fetch);
    const reload = route(); await fill(user, address);
    await user.keyboard("{Enter}");
    await screen.findByRole("heading", { name: "Settings destination" });
    expect(screen.getByText("/stores/returned-id/settings")).toBeTruthy();
    expect(reload).toHaveBeenCalledOnce();
    expect(fetch).toHaveBeenCalledOnce();
    expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores");
    expect(fetch.mock.calls[0][1].method).toBe("POST");
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({
      name: "Wooden Home", hostName: expected, currency: "CZK", culture: "cs-CZ", theme: created.theme,
    });
  });

  it("retains invalid and overlong drafts, blocks the request and links validation to the address field", async () => {
    const user = userEvent.setup(); const save = vi.spyOn(api, "createStore"); route();
    await fill(user, "a".repeat(64));
    await user.click(screen.getByRole("button", { name: "Create draft store" }));
    expect(save).not.toHaveBeenCalled();
    const address = screen.getByRole("textbox", { name: "Address name" });
    expect(address).toHaveProperty("value", "a".repeat(64));
    expect(address.getAttribute("aria-invalid")).toBe("true");
    await waitFor(() => expect(document.activeElement?.id).toBe("new-store-validation"));
    await user.click(screen.getByRole("button", { name: /^Address name: Use 1–63/ }));
    expect(document.activeElement).toBe(address);
    await user.clear(address); await user.type(address, "wooden--home");
    await user.click(screen.getByRole("button", { name: "Create draft store" }));
    expect(save).not.toHaveBeenCalled();
    expect(address).toHaveProperty("value", "wooden--home");
  });

  it.each([
    { language: "en", error: taken, message: "This address name is already taken. Choose another name." },
    { language: "cs", error: taken, message: "Tento název v adrese je již obsazený. Zvolte jiný název." },
    { language: "en", error: unavailable, message: "The platform cannot assign store addresses yet. Ask the platform operator to configure them, then try again." },
    { language: "cs", error: unavailable, message: "Platforma zatím nemůže přidělovat adresy obchodů. Požádejte správce platformy o nastavení a pak to zkuste znovu." },
  ])("retains a failed draft with localized field guidance in $language ($message)", async ({ language, error, message }) => {
    const user = userEvent.setup(); await i18n.changeLanguage(language);
    const save = vi.spyOn(api, "createStore").mockRejectedValueOnce(error).mockResolvedValue(created);
    const reload = route();
    const address = screen.getByRole("textbox", { name: language === "en" ? "Address name" : "Název v adrese" });
    await user.type(screen.getByRole("textbox", { name: language === "en" ? "Name" : "Název" }), "Wooden Home");
    await user.type(address, "wooden-home");
    await user.click(screen.getByRole("button", { name: language === "en" ? "Create draft store" : "Vytvořit koncept obchodu" }));
    const guidance = await screen.findByRole("button", { name: message });
    expect(address).toHaveProperty("value", "wooden-home");
    expect(address.getAttribute("aria-invalid")).toBe("true");
    expect(address.getAttribute("aria-describedby")).toContain("hostName-error");
    expect(reload).not.toHaveBeenCalled();
    await user.click(guidance); expect(document.activeElement).toBe(address);
    await user.click(screen.getByRole("button", { name: language === "en" ? "Create draft store" : "Vytvořit koncept obchodu" }));
    await screen.findByRole("heading", { name: "Settings destination" });
    expect(save.mock.calls.map(([input]) => input.hostName)).toEqual(["wooden-home", "wooden-home"]);
  });

  it("leaves domain ownership to the server and retains a refused custom domain", async () => {
    const user = userEvent.setup();
    const fetch = vi.fn().mockResolvedValue(Response.json({ errors: { hostName: ["A name under the configured platform suffix is required."] } }, { status: 400 }));
    vi.stubGlobal("fetch", fetch); route(); await fill(user, "somebody-elses-shop.cz");
    await user.click(screen.getByRole("button", { name: "Create draft store" }));
    await screen.findByRole("button", { name: /Use a short address name or a full address belonging to this platform/ });
    expect(screen.getByRole("textbox", { name: "Address name" })).toHaveProperty("value", "somebody-elses-shop.cz");
    expect(JSON.parse(fetch.mock.calls[0][1].body).hostName).toBe("somebody-elses-shop.cz");
    expect(fetch).toHaveBeenCalledOnce();
    expect(screen.getByText("/stores/new")).toBeTruthy();
  });

  it("preserves other field errors and the support reference alongside address guidance", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "createStore").mockRejectedValue(new HttpError("http", 400, {
      errors: { name: ["Invalid name"], hostName: ["Invalid platform address"] },
    }, "fv02-reference"));
    route(); await fill(user); await user.click(screen.getByRole("button", { name: "Create draft store" }));
    await screen.findByRole("button", { name: /Use a short address name/ });
    const name = screen.getByRole("textbox", { name: "Name" });
    expect(name.getAttribute("aria-invalid")).toBe("true");
    await user.click(screen.getByRole("button", { name: i18n.t("validation:nameInvalid") }));
    expect(document.activeElement).toBe(name);
    expect(screen.getByText(/fv02-reference/)).toBeTruthy();
  });

  it("keeps an unknown plan refusal generic rather than calling the name taken", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "createStore").mockRejectedValue(new HttpError("http", 409, { title: "This plan allows no more stores" }));
    route(); await fill(user); await user.click(screen.getByRole("button", { name: "Create draft store" }));
    await screen.findByRole("alert");
    expect(screen.queryByText("This address name is already taken. Choose another name.")).toBeNull();
    expect(screen.getByRole("textbox", { name: "Address name" })).toHaveProperty("value", "wooden-home");
  });

  it("serializes pending creation and does not redirect after leaving the form", async () => {
    const user = userEvent.setup(); let resolve!: (store: AdminStore) => void;
    const save = vi.spyOn(api, "createStore").mockReturnValue(new Promise((done) => { resolve = done; }));
    route(); await fill(user);
    const form = screen.getByRole("button", { name: "Create draft store" }).closest("form")!;
    await user.click(screen.getByRole("button", { name: "Create draft store" }));
    fireEvent.submit(form);
    expect(save).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Creating store…" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("textbox", { name: "Address name" }).closest("fieldset")).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("link", { name: "Leave creation" }));
    await act(async () => resolve(created));
    expect(screen.getByRole("heading", { name: "Stores destination" })).toBeTruthy();
    expect(screen.getByText("/stores")).toBeTruthy();
  });
});

describe("store creation route permissions", () => {
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("honors the %s role on a direct route", async (role) => {
    vi.spyOn(api, "me").mockResolvedValue({ id: "user-a", email: "staff@example.test", role, tenantId: "tenant-a" });
    vi.spyOn(api, "stores").mockResolvedValue([]);
    const save = vi.spyOn(api, "createStore");
    window.history.replaceState(null, "", "/stores/new"); render(<App />);
    if (["Owner", "Admin"].includes(role)) {
      expect(await screen.findByRole("button", { name: "Create draft store" })).toBeTruthy();
    } else {
      expect(await screen.findByRole("heading", { name: i18n.t("errors:permissionTitle") })).toBeTruthy();
      expect(screen.queryByRole("button", { name: "Create draft store" })).toBeNull();
    }
    expect(save).not.toHaveBeenCalled();
  });
});
