// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore, type StoreReadiness } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { StorePublicationSection } from "./StorePublicationSection";

const store: AdminStore = { id: "store-a", name: "Example Store", currency: "CZK", culture: "cs-CZ", status: "draft",
  theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 }, logoUrl: "/logo", primaryHostName: "example.localhost", returnWindowDays: 14, company: null };
const ready: StoreReadiness = { isReady: true, checks: [
  ...["store.logo", "store.company", "store.primary-domain", "catalog.visible-products", "orders.payment-method", "orders.shipping-method", "orders.payment-connected", "orders.shipping-connected"]
    .map((check) => ({ check, isReady: true, problem: null, weight: "required" as const })),
  ...["catalog.tax-classes", "platform.email", "platform.email-sender"]
    .map((check) => ({ check, isReady: true, problem: null, weight: "advised" as const })),
] };
function failed(check: string, problem: string): StoreReadiness {
  return { isReady: false, checks: ready.checks.map((item) => item.check === check ? { ...item, isReady: false, problem } : item) };
}
function view(value = store, props: Partial<Parameters<typeof StorePublicationSection>[0]> = {}) {
  return <MemoryRouter><StorePublicationSection store={value} reloadStores={() => undefined} canManage {...props} /></MemoryRouter>;
}
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (error: unknown) => void;
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail; }); return { promise, resolve, reject }; }
async function review(user: ReturnType<typeof userEvent.setup>) {
  const opener = screen.getByRole("button", { name: "Review and publish" });
  await waitFor(() => expect(opener).toHaveProperty("disabled", false));
  await user.click(opener); return screen.getByRole("button", { name: "Publish store" });
}
function appReads() {
  vi.spyOn(api, "me").mockResolvedValue({ id: "owner", email: "owner@example.test", role: "Owner", tenantId: "tenant-a" });
  vi.spyOn(api, "stores").mockResolvedValue([store]);
  window.history.replaceState(null, "", "/stores/store-a/settings");
}

beforeAll(initializeI18n);
beforeEach(async () => { await i18n.changeLanguage("en"); vi.stubGlobal("CSS", { escape: (value: string) => value }); HTMLElement.prototype.scrollIntoView = vi.fn(); });
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

describe("server publication readiness", () => {
  it.each(["en", "cs"])("separates required checks from non-blocking tax/email advice in %s", async (language) => {
    await i18n.changeLanguage(language);
    const body = { ...ready, checks: ready.checks.map((check) => check.weight === "advised" ? { ...check, isReady: false, problem: "Deployment advisory" } : check) };
    const read = vi.spyOn(api, "storeReadiness").mockResolvedValue(body);
    const listings = vi.spyOn(api, "hasVisibleStoreProduct"); const methods = vi.spyOn(api, "paymentMethods");
    render(view({ ...store, logoUrl: null, company: null }));
    const button = screen.getByRole("button", { name: language === "en" ? "Review and publish" : "Zkontrolovat a zveřejnit" });
    await waitFor(() => expect(button).toHaveProperty("disabled", false));
    const required = screen.getByRole("region", { name: i18n.t("stores:readinessWeight.required") });
    const advised = screen.getByRole("region", { name: i18n.t("stores:readinessWeight.advised") });
    expect(within(required).getAllByText(i18n.t("stores:checkStatus.ready"))).toHaveLength(8);
    expect(within(advised).getAllByText(i18n.t("stores:checkStatus.missing"))).toHaveLength(3);
    expect(read).toHaveBeenCalledWith("store-a", expect.any(AbortSignal));
    expect(listings).not.toHaveBeenCalled(); expect(methods).not.toHaveBeenCalled();
  });

  it.each([
    ["orders.payment-connected", '"Card" (comgate) is switched on but not connected to an account.'],
    ["orders.payment-connected", '"Card" (stripe) is switched on, but this platform does not offer that provider.'],
    ["orders.shipping-connected", '"Z-BOX" (packeta) is switched on but not connected to an account.'],
    ["store.primary-domain", "The store has no proved primary domain."],
  ])("blocks publication on %s and retains the reported cause", async (check, problem) => {
    vi.spyOn(api, "storeReadiness").mockResolvedValue(failed(check, problem));
    const publish = vi.spyOn(api, "publishStore"); render(view());
    expect(await screen.findByText(problem)).toBeTruthy();
    const button = screen.getByRole("button", { name: "Review and publish" });
    expect(button).toHaveProperty("disabled", true); fireEvent.click(button);
    expect(publish).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Publish store" })).toBeNull();
    expect([...document.querySelectorAll(".readiness-list a")].every((link) => link.getAttribute("href") === `/stores/store-a/${check === "store.primary-domain" ? "domains" : "methods"}`)).toBe(true);
  });

  it("honors a false server verdict even if the individual checks are ready", async () => {
    vi.spyOn(api, "storeReadiness").mockResolvedValue({ ...ready, isReady: false }); render(view());
    await screen.findByText("The server reports that publication requirements are not met.");
    expect(screen.getByRole("button", { name: "Review and publish" })).toHaveProperty("disabled", true);
  });

  it("retains unknown checks without guessing a management route or interpreting markup", async () => {
    vi.spyOn(api, "storeReadiness").mockResolvedValue({ isReady: false, checks: [{ check: "toString", isReady: false, problem: "<script>provider detail</script>", weight: "required" }] });
    render(view()); await screen.findByText("Additional check: toString");
    expect(screen.getByText("<script>provider detail</script>")).toBeTruthy();
    expect(document.querySelector(".readiness-list script")).toBeNull();
    expect(document.querySelector(".readiness-list a")).toBeNull();
  });

  it("keeps loading and failed reads unknown until a successful retry", async () => {
    const user = userEvent.setup(); const first = deferred<StoreReadiness>();
    vi.spyOn(api, "storeReadiness").mockReturnValueOnce(first.promise).mockResolvedValue(ready);
    render(view()); const opener = screen.getByRole("button", { name: "Review and publish" });
    expect(opener).toHaveProperty("disabled", true);
    await act(async () => first.reject(new HttpError("http", 503)));
    await screen.findByRole("alert"); expect(opener).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(opener).toHaveProperty("disabled", false));
  });

  it("retains old rows as unknown after a failed refresh rather than claiming readiness", async () => {
    const user = userEvent.setup(); const refresh = deferred<StoreReadiness>();
    vi.spyOn(api, "storeReadiness").mockResolvedValueOnce(ready).mockReturnValue(refresh.promise);
    render(view()); await screen.findByText("The server reports that all publication requirements are met.");
    await user.click(screen.getByRole("button", { name: "Refresh readiness checks" }));
    expect(screen.getByRole("button", { name: "Review and publish" })).toHaveProperty("disabled", true);
    await act(async () => refresh.reject(new HttpError("http", 503)));
    await screen.findByRole("alert");
    expect(screen.getAllByText("Could not be checked")).toHaveLength(11);
    expect(screen.queryByText("The server reports that all publication requirements are met.")).toBeNull();
  });

  it("keeps a publish refusal visible and disables the old confirmation until a fresh read succeeds", async () => {
    const user = userEvent.setup(); const refresh = deferred<StoreReadiness>();
    const blocked = failed("orders.payment-connected", "Card credentials are no longer readable.");
    vi.spyOn(api, "storeReadiness").mockResolvedValueOnce(ready).mockReturnValueOnce(refresh.promise).mockResolvedValue(ready);
    const publish = vi.spyOn(api, "publishStore").mockRejectedValue(new HttpError("http", 409, { readiness: blocked.checks, problems: ["Card credentials are no longer readable."] }));
    const reload = vi.fn(); render(view(store, { reloadStores: reload }));
    const confirm = await review(user); await waitFor(() => expect(document.activeElement).toBe(confirm));
    await user.click(confirm); await screen.findByText("Card credentials are no longer readable.");
    expect(confirm).toHaveProperty("disabled", true);
    expect(reload).toHaveBeenCalledOnce(); fireEvent.click(confirm); expect(publish).toHaveBeenCalledOnce();
    await act(async () => refresh.reject(new HttpError("http", 503)));
    expect(screen.getByText("Card credentials are no longer readable.")).toBeTruthy();
    expect(confirm).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Refresh readiness checks" }));
    await waitFor(() => expect(confirm).toHaveProperty("disabled", false));
    expect(screen.queryByText("Card credentials are no longer readable.")).toBeNull();
  });

  it("serializes publication and displays the confirmed response while refreshing store and readiness", async () => {
    const user = userEvent.setup(); const write = deferred<AdminStore>(); const refresh = deferred<StoreReadiness>();
    const read = vi.spyOn(api, "storeReadiness").mockResolvedValueOnce(ready).mockReturnValue(refresh.promise);
    const publish = vi.spyOn(api, "publishStore").mockReturnValue(write.promise); const reload = vi.fn();
    render(view(store, { reloadStores: reload })); const confirm = await review(user);
    fireEvent.click(confirm); fireEvent.click(confirm); expect(publish).toHaveBeenCalledOnce();
    await screen.findByRole("button", { name: "Updating…" });
    await act(async () => write.resolve({ ...store, status: "published" }));
    expect(await screen.findByText("Example Store is published.")).toBeTruthy();
    expect(screen.getByText("Published")).toBeTruthy(); expect(reload).toHaveBeenCalledOnce();
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    await act(async () => refresh.resolve(ready));
    expect(screen.getByRole("button", { name: "Review unpublishing" })).toBeTruthy();
  });

  it("keeps a published store's degradation visible while permitting explicit unpublication", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "storeReadiness").mockResolvedValue(failed("orders.shipping-connected", "Delivery credentials are unavailable."));
    const unpublish = vi.spyOn(api, "unpublishStore").mockResolvedValue(store); const reload = vi.fn();
    render(view({ ...store, status: "published" }, { reloadStores: reload }));
    expect(await screen.findByText("This store is published, but required checks now fail. Review its configuration; publication has not been changed.")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Review unpublishing" }));
    await user.click(screen.getByRole("button", { name: "Unpublish store" }));
    expect(await screen.findByText("Example Store is now a draft.")).toBeTruthy();
    expect(unpublish).toHaveBeenCalledOnce(); expect(reload).toHaveBeenCalledOnce();
  });

  it("hides mutations for read-only users while keeping check refresh available", async () => {
    const user = userEvent.setup(); const read = vi.spyOn(api, "storeReadiness").mockResolvedValue(ready);
    const publish = vi.spyOn(api, "publishStore"); render(view(store, { canManage: false }));
    await screen.findByText("The server reports that all publication requirements are met.");
    expect(screen.queryByRole("button", { name: "Review and publish" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Refresh readiness checks" }));
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2)); expect(publish).not.toHaveBeenCalled();
  });

  it.each([{ storeRefreshing: true }, { storeRefreshError: new HttpError("http", 503) }])("blocks writes when store status is stale: %j", async (props) => {
    vi.spyOn(api, "storeReadiness").mockResolvedValue(ready); render(view(store, props));
    await screen.findByText("The server reports that all publication requirements are met.");
    expect(screen.getByRole("button", { name: "Review and publish" })).toHaveProperty("disabled", true);
    expect(screen.getByText(/Refresh store details before changing publication/)).toBeTruthy();
  });

  it("does not carry an old store's refusal or unresolved checks into a different store", async () => {
    const first = deferred<StoreReadiness>();
    const read = vi.spyOn(api, "storeReadiness").mockReturnValueOnce(first.promise).mockResolvedValue(ready);
    const result = render(view()); result.rerender(view({ ...store, id: "store-b" }));
    await screen.findByText("The server reports that all publication requirements are met.");
    expect(read).toHaveBeenLastCalledWith("store-b", expect.any(AbortSignal));
    await act(async () => first.resolve(failed("orders.payment-connected", "Old-store failure")));
    expect(screen.queryByText("Old-store failure")).toBeNull();
  });

  it("rechecks readiness after saving store settings through the actual settings route", async () => {
    const user = userEvent.setup(); appReads(); const updated = { ...store, name: "Changed store" };
    vi.mocked(api.stores).mockResolvedValueOnce([store]).mockResolvedValue([updated]);
    vi.spyOn(api, "updateStore").mockResolvedValue(updated);
    const read = vi.spyOn(api, "storeReadiness").mockResolvedValueOnce(ready).mockResolvedValue(failed("orders.payment-connected", "Account requires attention."));
    render(<App />); await screen.findByText("The server reports that all publication requirements are met.");
    const name = screen.getByRole("textbox", { name: "Name" }); await user.clear(name); await user.type(name, "Changed store");
    await user.click(screen.getByRole("button", { name: "Save settings" }));
    expect(await screen.findByText("Account requires attention.")).toBeTruthy();
    expect(read).toHaveBeenCalledTimes(2);
    expect(screen.getByRole("button", { name: "Review and publish" })).toHaveProperty("disabled", true);
  });

  it("rechecks after logo replacement even when the returned logo URL is unchanged", async () => {
    const user = userEvent.setup(); appReads();
    vi.mocked(api.stores).mockResolvedValueOnce([store]).mockResolvedValue([{ ...store }]);
    vi.spyOn(api, "uploadLogo").mockResolvedValue(undefined);
    const read = vi.spyOn(api, "storeReadiness").mockResolvedValue(ready);
    render(<App />); await screen.findByText("The server reports that all publication requirements are met.");
    await user.upload(screen.getByLabelText("Choose replacement"), new File(["image"], "logo.png", { type: "image/png" }));
    await user.click(screen.getByRole("button", { name: "Upload selected logo" }));
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2));
  });

  it("reads current provider readiness when returning from the methods route", async () => {
    appReads();
    const read = vi.spyOn(api, "storeReadiness").mockResolvedValueOnce(ready).mockResolvedValue(failed("orders.shipping-connected", "Packeta account requires attention."));
    vi.spyOn(api, "paymentProviders").mockResolvedValue(["manual"]); vi.spyOn(api, "shippingProviders").mockResolvedValue(["packeta"]);
    vi.spyOn(api, "paymentMethods").mockResolvedValue([]); vi.spyOn(api, "shippingMethods").mockResolvedValue([]); vi.spyOn(api, "pickupPoints").mockResolvedValue([]);
    render(<App />); await screen.findByText("The server reports that all publication requirements are met.");
    await act(async () => { window.history.pushState(null, "", "/stores/store-a/methods"); window.dispatchEvent(new PopStateEvent("popstate")); });
    await screen.findByRole("heading", { name: i18n.t("methods:title"), level: 1 });
    await act(async () => { window.history.pushState(null, "", "/stores/store-a/settings"); window.dispatchEvent(new PopStateEvent("popstate")); });
    await screen.findByText("Packeta account requires attention."); expect(read).toHaveBeenCalledTimes(2);
  });

  it("retains a confirmed publication while a failed store refresh blocks another change", async () => {
    const user = userEvent.setup(); appReads();
    vi.mocked(api.stores).mockResolvedValueOnce([store]).mockRejectedValue(new HttpError("http", 503));
    vi.spyOn(api, "storeReadiness").mockResolvedValue(ready);
    vi.spyOn(api, "publishStore").mockResolvedValue({ ...store, status: "published" });
    render(<App />); await screen.findByText("The server reports that all publication requirements are met.");
    await user.click(await review(user));
    await screen.findByRole("alert");
    expect(screen.getByText("Published")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Review unpublishing" })).toHaveProperty("disabled", true);
    expect(screen.getByText(/Refresh store details before changing publication/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Try again" })).toBeTruthy();
  });

  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("keeps the actual settings route's publication controls correct for %s", async (role) => {
    const user = userEvent.setup(); appReads();
    vi.mocked(api.me).mockResolvedValue({ id: "staff", email: "staff@example.test", role, tenantId: "tenant-a" });
    const read = vi.spyOn(api, "storeReadiness").mockResolvedValue(ready);
    const publish = vi.spyOn(api, "publishStore"); render(<App />);
    await screen.findByText("The server reports that all publication requirements are met.");
    const button = screen.queryByRole("button", { name: "Review and publish" });
    if (["Owner", "Admin"].includes(role)) expect(button).toHaveProperty("disabled", false);
    else expect(button).toBeNull();
    const refresh = screen.getByRole("button", { name: "Refresh readiness checks" });
    expect(refresh).toHaveProperty("disabled", false); await user.click(refresh);
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2)); expect(publish).not.toHaveBeenCalled();
  });

  it("restores opener focus when publication is cancelled", async () => {
    const user = userEvent.setup(); vi.spyOn(api, "storeReadiness").mockResolvedValue(ready); render(view());
    await review(user); await user.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Review and publish" })));
  });

  it("does not claim publication from a mismatched success response", async () => {
    const user = userEvent.setup(); vi.spyOn(api, "storeReadiness").mockResolvedValue(ready);
    vi.spyOn(api, "publishStore").mockResolvedValue({ ...store, id: "another-store", status: "published" });
    render(view()); await user.click(await review(user)); await screen.findByRole("alert");
    expect(screen.queryByText("Example Store is published.")).toBeNull();
    expect(screen.getByText("Draft")).toBeTruthy();
  });
});
