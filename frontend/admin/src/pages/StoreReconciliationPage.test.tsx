// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { HttpError } from "../api/http";
import { reconciliationApi, reportDate, type ReconciliationReport } from "../api/reconciliation";
import { initializeI18n, i18n } from "../i18n";
import { SessionContext } from "../session";
import { metadataStore } from "../test/metadataFixtures";
import { reconciliationFixture } from "../test/reconciliationFixtures";
import { uiLocale } from "../utils/format";
import { StoreReconciliationPage } from "./StoreReconciliationPage";

const account = (role = "Owner") => ({ id: "owner", tenantId: "tenant", role, email: "owner@example.test" });
function Shell({ initialRole }: { initialRole: string }) {
  const [store, setStore] = useState(metadataStore), [user, setUser] = useState(account(initialRole));
  const location = useLocation(), navigate = useNavigate();
  return <SessionContext value={{ user, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => setStore({ ...metadataStore, id: "store-b" })}>Other store</button>
    <button onClick={() => setUser({ ...user, role: "Support" })}>Other role</button>
    <button onClick={() => setUser({ ...user, id: "other-user" })}>Other session</button>
    <button onClick={() => navigate(-1)}>Back</button><output>{location.search}</output>
    <Outlet context={{ store, storeRefreshing: false, storeRefreshError: null, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function route(role = "Owner", query = "") {
  return render(<MemoryRouter initialEntries={["/report" + query]}><Routes><Route element={<Shell initialRole={role} />}><Route path="report" element={<StoreReconciliationPage />} /></Route></Routes></MemoryRouter>);
}
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); vi.stubGlobal("CSS", { escape: (value: string) => value }); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.spyOn(reconciliationApi, "read").mockImplementation(async (_store, range) => reconciliationFixture(range ?? undefined));
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.history.replaceState(null, "", "/"); });
it.each(["en", "cs"])("shows actual source/classification and inert details, without relabelling numeric money in %s", async language => {
  await i18n.changeLanguage(language); route();
  await screen.findByRole("heading", { name: i18n.t("reconciliation:source") });
  for (const kind of Object.keys(i18n.getResourceBundle(language, "reconciliation").kinds)) expect(screen.getByText(i18n.t("reconciliation:kinds." + kind, { defaultValue: kind }))).toBeTruthy();
  expect(screen.getByText("ShopForge asked for 10.00 EUR; statement confirms 10.00 CZK.")).toBeTruthy();
  expect(screen.getByRole("link", { name: i18n.t("reconciliation:unapplied") }).getAttribute("href")).toBe(`/stores/${metadataStore.id}/unapplied-payments`);
  expect(screen.getAllByRole("link", { name: i18n.t("reconciliation:order", { number: "2026/17" }) })[0]!.getAttribute("href")).toBe(`/stores/${metadataStore.id}/orders/2026%2F17`);
  expect(document.querySelector(".reconciliation-page script")).toBeNull(); expect(screen.queryByText(/98.?765/)).toBeNull();
  expect(screen.getByText(i18n.t("reconciliation:coverage"))).toBeTruthy();
});
it("applies only ISO date fields, focuses results, retains range through history and resets to server defaults", async () => {
  const user = userEvent.setup(); route(); await screen.findByRole("heading", { name: "Statement data" });
  fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-10-25" } }); fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-10-26" } });
  await user.click(screen.getByRole("button", { name: "Apply period" }));
  await waitFor(() => expect(reconciliationApi.read).toHaveBeenLastCalledWith(metadataStore.id, { from: "2026-10-25", to: "2026-10-26" }, expect.any(AbortSignal), undefined));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Statement data" })));
  expect(screen.getByRole("status").textContent).toBe("?from=2026-10-25&to=2026-10-26");
  await user.click(screen.getByRole("button", { name: "Default period" })); await waitFor(() => expect(reconciliationApi.read).toHaveBeenLastCalledWith(metadataStore.id, null, expect.any(AbortSignal), undefined));
  await user.click(screen.getByRole("button", { name: "Back" })); await waitFor(() => expect(screen.getByLabelText("From")).toHaveProperty("value", "2026-10-25"));
});
it.each(["en", "cs"])("shows exact daily recorded counts, including zero and large counts, in %s", async language => {
  await i18n.changeLanguage(language);
  const data = reconciliationFixture(); data.days[0]!.paid = 0; data.days[1]!.paid = 1234567;
  vi.mocked(reconciliationApi.read).mockResolvedValue(data); route();
  const daily = await screen.findByRole("region", { name: i18n.t("reconciliation:dailyTitle") });
  const rows = within(daily).getAllByRole("listitem"); expect(rows).toHaveLength(14);
  const locale = uiLocale(language), first = within(rows[0]!), second = within(rows[1]!);
  expect(first.getByRole("heading").textContent).toBe(reportDate(data.from, locale));
  expect(first.getAllByRole("definition").map(value => value.textContent)).toEqual(["0", "7"]);
  expect(second.getAllByRole("definition").map(value => value.textContent)).toEqual([new Intl.NumberFormat(locale).format(1234567), "0"]);
  expect(within(daily).getAllByText(i18n.t("reconciliation:recordedPaid"))).toHaveLength(14);
  expect(within(daily).getByText(i18n.t("reconciliation:dailyHint"))).toBeTruthy();
  expect(daily.textContent).not.toMatch(/98.?765|EUR|CZK/);
  expect(reconciliationApi.read).toHaveBeenCalledTimes(1);
});
it.each(["en", "cs"])("reviews one day by keyboard and restores the period through history in %s", async language => {
  await i18n.changeLanguage(language); const user = userEvent.setup(); route();
  const daily = await screen.findByRole("region", { name: i18n.t("reconciliation:dailyTitle") });
  within(daily).getByRole("button", { name: i18n.t("reconciliation:reviewDay", { date: reportDate("2026-10-10", uiLocale(language)) }) }).focus();
  await user.keyboard("{Enter}");
  await waitFor(() => expect(reconciliationApi.read).toHaveBeenLastCalledWith(metadataStore.id, { from: "2026-10-10", to: "2026-10-10" }, expect.any(AbortSignal), undefined));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { name: i18n.t("reconciliation:source") })));
  expect(screen.getByRole("status").textContent).toBe("?from=2026-10-10&to=2026-10-10");
  expect(within(screen.getByRole("region", { name: i18n.t("reconciliation:dailyTitle") })).getAllByRole("listitem")).toHaveLength(1);
  await user.click(screen.getByRole("button", { name: "Back" }));
  await waitFor(() => expect(within(screen.getByRole("region", { name: i18n.t("reconciliation:dailyTitle") })).getAllByRole("listitem")).toHaveLength(14));
});
it("retains all 31 calendar dates across the provider's daylight-saving boundary", async () => {
  route("Owner", "?from=2026-10-01&to=2026-10-31");
  const daily = await screen.findByRole("region", { name: "Recorded payments by day" });
  expect(Array.from(daily.querySelectorAll("time")).map(time => time.dateTime)).toEqual(Array.from({ length: 31 }, (_, index) => `2026-10-${String(index + 1).padStart(2, "0")}`));
  expect(reconciliationApi.read).toHaveBeenCalledExactlyOnceWith(metadataStore.id, { from: "2026-10-01", to: "2026-10-31" }, expect.any(AbortSignal), undefined);
});
it("keeps recorded zero counts distinct from unavailable statement coverage", async () => {
  const data = reconciliationFixture(); data.statementsThrough = null; data.unexplained = 0; data.differences = [];
  data.days.forEach(day => { day.paid = 0; day.unexplained = 0; });
  vi.mocked(reconciliationApi.read).mockResolvedValue(data); route();
  const daily = await screen.findByRole("region", { name: "Recorded payments by day" });
  expect(within(daily).getAllByRole("definition").every(value => value.textContent === "0")).toBe(true);
  expect(screen.getByText("Not available")).toBeTruthy(); expect(screen.getByText(i18n.t("reconciliation:coverage"))).toBeTruthy();
});
it("can revalidate a day from an explicitly stale snapshot without replaying any action", async () => {
  const user = userEvent.setup(); route(); await screen.findByRole("heading", { name: "Statement data" });
  vi.mocked(reconciliationApi.read).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(screen.getByRole("button", { name: "Refresh statement view" })); await screen.findByText(i18n.t("reconciliation:stale"));
  await user.click(screen.getByRole("button", { name: "Review 5 Oct 2026" }));
  await waitFor(() => expect(reconciliationApi.read).toHaveBeenLastCalledWith(metadataStore.id, { from: "2026-10-05", to: "2026-10-05" }, expect.any(AbortSignal), undefined));
  await waitFor(() => expect(screen.queryByRole("alert")).toBeNull()); expect(reconciliationApi.read).toHaveBeenCalledTimes(3);
});
it("blocks invalid URL periods without a read and lets the user recover", async () => {
  const user = userEvent.setup(); route("Owner", "?from=2026-10-31&to=2026-10-01");
  await screen.findByRole("alert"); expect(reconciliationApi.read).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Default period" })); await screen.findByRole("heading", { name: "Statement data" });
});
it("validates an edited range before a request and retains the draft", async () => {
  const user = userEvent.setup(); route(); await screen.findByRole("heading", { name: "Statement data" });
  fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-11-01" } }); await user.click(screen.getByRole("button", { name: "Apply period" }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("alert"))); expect(screen.getByLabelText("To")).toHaveProperty("value", "2026-11-01"); expect(reconciliationApi.read).toHaveBeenCalledTimes(1);
});
it("maps only the refused server field and preserves the applied period", async () => {
  vi.mocked(reconciliationApi.read).mockRejectedValueOnce(new HttpError("http", 400, { errors: { to: ["private diagnostic"] } }));
  const user = userEvent.setup(); route("Owner", "?from=2026-10-01&to=2026-10-14");
  await user.click(await screen.findByRole("button", { name: "To: " + i18n.t("reconciliation:invalidRange") }));
  expect(document.activeElement).toBe(screen.getByLabelText("To"));
  expect(screen.getByLabelText("From").getAttribute("aria-invalid")).toBeNull();
  expect(screen.getByLabelText("To")).toHaveProperty("value", "2026-10-14");
  expect(screen.queryByText("private diagnostic")).toBeNull();
});
it("never treats empty/no-import data as completed reconciliation", async () => {
  const data = reconciliationFixture(); data.statementsThrough = null; data.unexplained = 0; data.differences = []; data.days.forEach(day => { day.unexplained = 0; });
  vi.mocked(reconciliationApi.read).mockResolvedValue(data); route();
  expect(await screen.findByText("Not available")).toBeTruthy(); expect(screen.getByText(i18n.t("reconciliation:empty"))).toBeTruthy();
});
it("shows unknown kinds literally with no invented action", async () => {
  const data = reconciliationFixture(); data.differences[0]!.kind = "future <kind>"; vi.mocked(reconciliationApi.read).mockResolvedValue(data); route();
  expect(await screen.findByText("Other statement entry")).toBeTruthy(); expect(screen.getByText("future <kind>")).toBeTruthy();
});
it("bounds large exception groups and keeps every entry reachable without extra requests", async () => {
  const data = reconciliationFixture();
  data.differences = Array.from({ length: 60 }, (_, index) => ({ ...data.differences[2]!, transactionId: "LARGE-" + index }));
  data.unexplained = 60; data.days[0]!.unexplained = 60; vi.mocked(reconciliationApi.read).mockResolvedValue(data);
  const user = userEvent.setup(); route(); await screen.findByText("LARGE-0");
  expect(screen.queryByText("LARGE-25")).toBeNull();
  await user.click(screen.getByRole("button", { name: "Next entries" })); expect(await screen.findByText("LARGE-25")).toBeTruthy(); expect(screen.queryByText("LARGE-0")).toBeNull();
  await user.click(screen.getByRole("button", { name: "Next entries" })); expect(await screen.findByText("LARGE-59")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Next entries" })).toHaveProperty("disabled", true); expect(reconciliationApi.read).toHaveBeenCalledTimes(1);
});
it.each([403, 404])("removes retained financial references when refresh returns %s", async status => {
  const user = userEvent.setup(); route(); await screen.findByRole("heading", { name: "Statement data" }); vi.mocked(reconciliationApi.read).mockRejectedValueOnce(new HttpError("http", status));
  await user.click(screen.getByRole("button", { name: "Refresh statement view" })); await screen.findByRole("alert"); expect(screen.queryByText("TX-0")).toBeNull(); expect(screen.queryByRole("heading", { name: "Statement data" })).toBeNull();
  expect(screen.queryByRole("region", { name: "Recorded payments by day" })).toBeNull();
});
it("marks a retained snapshot stale after a read failure and retries only explicitly", async () => {
  const user = userEvent.setup(); route(); await screen.findByRole("heading", { name: "Statement data" }); vi.mocked(reconciliationApi.read).mockRejectedValueOnce(new HttpError("network", null));
  await user.click(screen.getByRole("button", { name: "Refresh statement view" })); await screen.findByText(i18n.t("reconciliation:stale")); expect(screen.getByText("TX-0")).toBeTruthy(); expect(reconciliationApi.read).toHaveBeenCalledTimes(2);
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.queryByRole("alert")).toBeNull()); expect(reconciliationApi.read).toHaveBeenCalledTimes(3);
});
it.each(["Other store", "Other session", "Other role"])("discards a late read after %s", async action => {
  const user = userEvent.setup(); let resolve!: (value: ReconciliationReport) => void; let signal!: AbortSignal;
  vi.mocked(reconciliationApi.read).mockImplementationOnce((_store, _range, nextSignal) => { signal = nextSignal!; return new Promise(yes => { resolve = yes; }); }); route();
  await waitFor(() => expect(reconciliationApi.read).toHaveBeenCalledTimes(1)); await user.click(screen.getByRole("button", { name: action }));
  const late = reconciliationFixture(); late.provider = "OLD PRIVATE PROVIDER"; await act(async () => resolve(late)); expect(signal.aborted).toBe(true); expect(screen.queryByText("OLD PRIVATE PROVIDER")).toBeNull();
});
it.each(["Owner", "Admin", "CatalogManager", "Warehouse", "Support", "unknown"])("gates the actual report route and navigation for %s", async role => {
  vi.spyOn(api, "me").mockResolvedValue(account(role)); vi.spyOn(api, "stores").mockResolvedValue([metadataStore]); window.history.replaceState(null, "", `/stores/${metadataStore.id}/reconciliation`); render(<App />);
  const allowed = ["Owner", "Admin"].includes(role);
  await screen.findByRole("heading", { name: allowed ? "Statement data" : i18n.t("errors:permissionTitle") });
  await waitFor(() => expect(reconciliationApi.read).toHaveBeenCalledTimes(allowed ? 1 : 0));
  expect(screen.queryByRole("link", { name: "Payment check" }) !== null).toBe(allowed);
});
