// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api, type CurrentUser } from "../api";
import { HttpError } from "../api/http";
import { planUsageApi, type PlanUsage } from "../api/planUsage";
import { i18n, initializeI18n } from "../i18n";
import en from "../i18n/locales/en";
import { SessionContext } from "../session";
import { planStaff, planUsageFixture } from "../test/planUsageFixtures";
import { PlanUsagePage } from "./PlanUsagePage";

let current: CurrentUser, data: PlanUsage;
const word = (key: keyof typeof en.resources.planUsage) => i18n.t(`planUsage:${key}`);
function Shop({ user = planStaff }: { user?: CurrentUser }) {
  return <SessionContext value={{ user, logout: async () => {}, logoutPending: false, logoutError: null }}><MemoryRouter initialEntries={["/plan"]}><Link to="/products">Leave usage</Link><Routes><Route path="/plan" element={<PlanUsagePage />} /><Route path="/products" element={<h1>Product destination</h1>} /></Routes></MemoryRouter></SessionContext>;
}
function section(key: "stores" | "products" | "otherUsage") { return within(screen.getByRole("region", { name: word(key) })); }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); current = { ...planStaff }; data = structuredClone(planUsageFixture);
  vi.spyOn(planUsageApi, "session").mockImplementation(async () => ({ ...current })); vi.spyOn(planUsageApi, "read").mockImplementation(async () => structuredClone(data)); HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

it("shows authoritative company figures with physical products separate from listings and staff totals", async () => {
  render(<Shop />); await screen.findByText("Starter"); expect(section("stores").getAllByText("2", { exact: true })).toHaveLength(2); expect(section("stores").getByText(word("at"))).toBeTruthy(); expect(section("products").getByText("42")).toBeTruthy(); expect(section("products").getByText("100")).toBeTruthy(); expect(section("products").getByText("Remaining capacity: 58")).toBeTruthy(); expect(section("otherUsage").getByText("156")).toBeTruthy(); expect(section("otherUsage").getByText(word("users"))).toBeTruthy(); expect(planUsageApi.session).toHaveBeenCalledTimes(2); expect(planUsageApi.read).toHaveBeenCalledTimes(1); expect(document.querySelector('a[href*="billing"],a[href*="upgrade"]')).toBeNull();
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "FutureRole"])("opens the actual plan route/navigation for %s independently of store selection", async role => {
  current.role = role; vi.spyOn(api, "me").mockResolvedValue(current); vi.spyOn(api, "stores").mockResolvedValue([]); window.history.replaceState(null, "", "/plan"); render(<App />); await screen.findByText("Starter"); expect(screen.getByRole("link", { name: word("title") }).getAttribute("href")).toBe("/plan"); await waitFor(() => expect(planUsageApi.session).toHaveBeenCalledTimes(2)); expect(planUsageApi.read).toHaveBeenCalledTimes(1);
});
it("keeps null caps distinct from zero caps and never invents the default plan’s identity", async () => {
  data = { code: null, name: null, maxStores: null, maxProducts: 0, usage: [{ name: "stores", value: 12 }, { name: "products", value: 0 }] }; render(<Shop />); await screen.findByText(word("uncapped")); expect(screen.getAllByText(word("notSupplied"))).toHaveLength(2); expect(section("stores").getByText(word("noCap"))).toBeTruthy(); expect(section("products").getByText(word("at"))).toBeTruthy(); expect(section("products").getAllByText("0")).toHaveLength(2); expect(screen.queryByText("Starter")).toBeNull();
});
it("renders over-cap usage without clipping counts or implying existing records were removed", async () => {
  data.maxStores = 1; render(<Shop />); await screen.findByText("Starter"); expect(section("stores").getByText("2")).toBeTruthy(); expect(section("stores").getByText(word("over"))).toBeTruthy(); expect(screen.getByText(word("overHint"))).toBeTruthy(); expect(screen.getByRole("link", { name: word("viewStores") }).getAttribute("href")).toBe("/stores");
});
it("does not replace missing counts with zero, or discard unknown metric names", async () => {
  data.usage = [{ name: "future <metric>", value: 1234 }]; render(<Shop />); await screen.findByText("Starter"); expect(screen.getAllByText(word("notReported"))).toHaveLength(2); expect(screen.getAllByText(word("unknown"))).toHaveLength(2); expect(section("otherUsage").getByText("future <metric>")).toBeTruthy(); expect(section("otherUsage").getByText("1,234")).toBeTruthy(); expect(document.querySelector("metric")).toBeNull(); expect(screen.queryByText(word("usersMeaning"))).toBeNull();
});
it("refreshes once on explicit action, hiding the old snapshot until the new identity-checked result arrives", async () => {
  const user = userEvent.setup(); render(<Shop />); await screen.findByText("Starter"); const held = deferred<PlanUsage>(); vi.mocked(planUsageApi.read).mockReturnValueOnce(held.promise); await user.click(screen.getByRole("button", { name: word("refresh") })); expect(screen.queryByText("Starter")).toBeNull(); fireEvent.click(screen.getByRole("button", { name: word("loading") })); await waitFor(() => expect(planUsageApi.read).toHaveBeenCalledTimes(2)); await act(async () => held.resolve({ ...data, name: "Growth" })); await screen.findByText("Growth"); expect(screen.getByText(word("refreshed"))).toBeTruthy(); expect(document.activeElement?.textContent).toContain(word("refreshed"));
});
it.each([401, 403])("hides the snapshot on refresh status %s and offers fixed auth recovery", async code => {
  const user = userEvent.setup(); render(<Shop />); await screen.findByText("Starter"); vi.mocked(planUsageApi.read).mockRejectedValueOnce(new HttpError("http", code)); await user.click(screen.getByRole("button", { name: word("refresh") })); await screen.findByText(word(code === 401 ? "expired" : "forbidden")); expect(screen.queryByText("Starter")).toBeNull(); expect(screen.getByRole("link", { name: word("reload") }).getAttribute("href")).toMatch(/\/plan$/);
});
it.each([429, 503])("offers explicit GET retry after %s without retaining old totals or auto retrying", async code => {
  vi.mocked(planUsageApi.read).mockRejectedValueOnce(new HttpError("http", code)); const user = userEvent.setup(); render(<Shop />); await screen.findByRole("alert"); expect(screen.queryByText("Starter")).toBeNull(); expect(planUsageApi.read).toHaveBeenCalledTimes(1); await user.click(screen.getByRole("button", { name: i18n.t("common:retry") })); await screen.findByText("Starter"); expect(planUsageApi.read).toHaveBeenCalledTimes(2);
});
it.each(["id", "tenantId", "role", "email"] as const)("refuses to read another company/account after initial %s changes", async field => {
  current = { ...current, [field]: "different" }; render(<Shop />); await screen.findByText(word("changed")); expect(planUsageApi.read).not.toHaveBeenCalled();
});
it("does not reveal returned counts when the follow-up identity check changed", async () => {
  vi.mocked(planUsageApi.read).mockImplementationOnce(async () => { current.tenantId = "company-b"; return data; }); render(<Shop />); await screen.findByText(word("changed")); expect(screen.queryByText("Starter")).toBeNull();
});
it("drops late reads on departure without issuing a follow-up session read", async () => {
  const held = deferred<PlanUsage>(); vi.mocked(planUsageApi.read).mockReturnValueOnce(held.promise); const user = userEvent.setup(); render(<Shop />); await waitFor(() => expect(planUsageApi.read).toHaveBeenCalledTimes(1)); await user.click(screen.getByRole("link", { name: "Leave usage" })); expect(vi.mocked(planUsageApi.read).mock.calls[0][0].aborted).toBe(true); await act(async () => held.resolve(data)); expect(planUsageApi.session).toHaveBeenCalledTimes(1); expect(screen.queryByText("Starter")).toBeNull();
});
it("discards an old company’s pending response when rendered session scope moves", async () => {
  const held = deferred<PlanUsage>(); vi.mocked(planUsageApi.read).mockReturnValueOnce(held.promise); const page = render(<Shop />); await waitFor(() => expect(planUsageApi.read).toHaveBeenCalledTimes(1)); current = { ...current, tenantId: "company-b" }; data.name = "Company B"; page.rerender(<Shop user={current} />); await screen.findByText("Company B"); await act(async () => held.resolve({ ...data, name: "Company A" })); expect(screen.queryByText("Company A")).toBeNull(); expect(vi.mocked(planUsageApi.read).mock.calls[0][0].aborted).toBe(true);
});
it("formats counts in Czech after a language switch without refetching", async () => {
  data.maxProducts = null; data.usage = [{ name: "products", value: 1234 }]; render(<Shop />); await screen.findByText("1,234"); await act(async () => { await i18n.changeLanguage("cs"); }); expect(section("products").getByText(new Intl.NumberFormat("cs").format(1234), { normalizer: value => value })).toBeTruthy(); expect(planUsageApi.read).toHaveBeenCalledTimes(1);
});
it("finishes initial reads in StrictMode without introducing writes", async () => { render(<StrictMode><Shop /></StrictMode>); await screen.findByText("Starter"); });
