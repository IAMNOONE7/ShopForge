// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore, type CurrentUser } from "../api";
import { HttpError } from "../api/http";
import { tenantAuditApi, type AuditRecord } from "../api/tenantAudit";
import { i18n, initializeI18n } from "../i18n";
import en from "../i18n/locales/en";
import { SessionContext } from "../session";
import { auditRows, auditStaff, auditStore, defaultAuditQuery } from "../test/tenantAuditFixtures";
import type { RequestState } from "../useRequest";
import { maxAuditPage } from "../utils/auditQuery";
import { TenantAuditPage } from "./TenantAuditPage";

let current: CurrentUser, rows: AuditRecord[];
type Word = Exclude<keyof typeof en.resources.tenantAudit, "actorKinds" | "validation">;
const word = (key: Word) => i18n.t(`tenantAudit:${key}`);
const button = (key: Word) => screen.getByRole("button", { name: word(key) }) as HTMLButtonElement;
const field = (key: "from" | "to" | "actionFilter") => screen.getByLabelText(word(key)) as HTMLInputElement;
const readyStores: RequestState<AdminStore[]> = { status: "ready", data: [auditStore], refreshing: false, refreshError: null };
function Location() { const location = useLocation(), navigate = useNavigate(); return <><output data-testid="location">{location.pathname + location.search}</output><button onClick={() => void navigate(-1)}>Back in history</button><button onClick={() => void navigate(1)}>Forward in history</button></>; }
function Shop({ user = auditStaff, search = "", stores = readyStores }: { user?: CurrentUser; search?: string; stores?: RequestState<AdminStore[]> }) {
  return <SessionContext value={{ user, logout: async () => {}, logoutPending: false, logoutError: null }}><MemoryRouter initialEntries={["/audit" + search]}><Location /><Link to="/products">Leave activity</Link><Routes><Route element={<Outlet context={{ stores, reloadStores: vi.fn() }} />}><Route path="/audit" element={<TenantAuditPage />} /></Route><Route path="/products" element={<h1>Product destination</h1>} /></Routes></MemoryRouter></SessionContext>;
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
const loaded = () => screen.findByRole("article", { name: auditRows[0].action });
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); current = { ...auditStaff }; rows = structuredClone(auditRows);
  vi.spyOn(tenantAuditApi, "session").mockImplementation(async () => ({ ...current })); vi.spyOn(tenantAuditApi, "read").mockImplementation(async query => structuredClone(rows.slice((query.page - 1) * query.pageSize, query.page * query.pageSize))); HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

it("shows original actor/subject/time facts, literal shortened details and missing fields without inventing history", async () => {
  render(<Shop />); await loaded(); expect(screen.getByText("former-name@example.test")).toBeTruthy(); const first = within(screen.getByRole("article", { name: "order.refunded" })); expect(first.getByText("202600042")).toBeTruthy(); expect(first.getByText(/UTC/)).toBeTruthy(); expect(first.getByText(auditRows[0].details!)).toBeTruthy(); expect(first.getByText(auditRows[0].recordedAt)).toBeTruthy(); expect(screen.getByText(word("noDetails"))).toBeTruthy(); expect(screen.getByText(auditRows[2].details!)).toBeTruthy(); expect(document.querySelector("script")).toBeNull(); expect(tenantAuditApi.read).toHaveBeenCalledExactlyOnceWith(defaultAuditQuery, expect.any(AbortSignal)); expect(tenantAuditApi.session).toHaveBeenCalledTimes(2);
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "FutureRole"])("gates the actual route and navigation for %s", async role => {
  current.role = role; vi.spyOn(api, "me").mockResolvedValue(current); vi.spyOn(api, "stores").mockResolvedValue([auditStore]); window.history.replaceState(null, "", "/audit"); render(<App />); const allowed = ["Owner", "Admin"].includes(role);
  if (allowed) await loaded(); else await screen.findByRole("heading", { name: i18n.t("errors:permissionTitle") });
  await waitFor(() => expect(tenantAuditApi.read).toHaveBeenCalledTimes(allowed ? 1 : 0)); expect(screen.queryByRole("link", { name: word("title") }) !== null).toBe(allowed); if (!allowed) expect(tenantAuditApi.session).not.toHaveBeenCalled();
});
it("applies exact filters explicitly, encodes offsets, resets paging and preserves all filter values", async () => {
  const user = userEvent.setup(); render(<Shop search="?page=2&pageSize=2" />); await screen.findByText("<literal subject>"); await user.selectOptions(screen.getByLabelText(word("storeFilter")), auditStore.id); await user.type(field("actionFilter"), "order.refunded"); await user.type(field("from"), "2026-10-28T10:00:00+02:00"); await user.type(field("to"), "2026-10-28T09:00:00Z"); expect(tenantAuditApi.read).toHaveBeenCalledTimes(1); vi.mocked(tenantAuditApi.read).mockResolvedValueOnce([auditRows[0]]); await user.click(button("apply")); await loaded();
  expect(tenantAuditApi.read).toHaveBeenLastCalledWith({ storeId: auditStore.id, action: "order.refunded", from: "2026-10-28T10:00:00+02:00", to: "2026-10-28T09:00:00Z", page: 1, pageSize: 2 }, expect.any(AbortSignal)); const url = screen.getByTestId("location").textContent!; expect(url).toContain("%2B02%3A00"); expect(url).not.toContain("page=2"); expect(field("from").value).toBe("2026-10-28T10:00:00+02:00"); expect(document.activeElement?.getAttribute("role")).toBe("status");
});
it("validates fields and date order without changing the applied query or making another request", async () => {
  const user = userEvent.setup(); render(<Shop />); await loaded(); fireEvent.change(field("from"), { target: { value: "2026-02-30T00:00Z" } }); await user.click(button("apply")); await user.click(screen.getByRole("button", { name: i18n.t("tenantAudit:validation.from") })); expect(document.activeElement).toBe(field("from")); expect(tenantAuditApi.read).toHaveBeenCalledTimes(1);
  fireEvent.change(field("from"), { target: { value: "2026-10-28T08:00Z" } }); fireEvent.change(field("to"), { target: { value: "2026-10-28T08:00Z" } }); await user.click(button("apply")); await user.click(screen.getByRole("button", { name: i18n.t("tenantAudit:validation.range") })); expect(document.activeElement).toBe(field("to")); expect(screen.getByTestId("location").textContent).toBe("/audit");
});
it.each(["?page=0", "?pageSize=101", "?storeId=bad", "?from=bad", "?action=a&action=b", "?actor=somebody"])("never broadens an invalid or unsupported URL %s", async search => {
  render(<Shop search={search} />); await screen.findByText(word("invalidUrl")); expect(tenantAuditApi.read).not.toHaveBeenCalled(); expect(tenantAuditApi.session).not.toHaveBeenCalled(); expect(screen.queryByRole("article")).toBeNull();
});
it("lets a person explicitly review an ambiguous URL and apply the displayed supported fields", async () => {
  const user = userEvent.setup(); render(<Shop search="?action=order.refunded&action=other.action&tenantId=another-company" />); expect(field("actionFilter").value).toBe("order.refunded"); await user.click(button("apply")); await loaded(); expect(tenantAuditApi.read).toHaveBeenCalledTimes(1); expect(screen.getByTestId("location").textContent).toBe("/audit?action=order.refunded");
});
it("keeps a valid unlisted store ID from the URL when store names are unavailable", async () => {
  render(<Shop search={`?storeId=${auditStore.id.toUpperCase()}`} stores={{ status: "error", error: new Error("unavailable") }} />); await loaded(); expect(screen.getByLabelText(word("storeFilter"))).toHaveProperty("value", auditStore.id.toUpperCase()); expect(screen.getByText(word("storeNamesUnavailable"))).toBeTruthy(); expect(tenantAuditApi.read).toHaveBeenLastCalledWith(expect.objectContaining({ storeId: auditStore.id.toUpperCase() }), expect.any(AbortSignal));
});
it("selects an existing store from an uppercase GUID link without silently showing the all-stores option", async () => {
  render(<Shop search={`?storeId=${auditStore.id.toUpperCase()}`} />); await loaded(); expect(screen.getByLabelText(word("storeFilter"))).toHaveProperty("value", auditStore.id.toUpperCase()); expect(screen.getByRole("option", { name: auditStore.name })).toHaveProperty("selected", true);
});
it("labels both native selects independently and applies their values through the documented query", async () => {
  const user = userEvent.setup(); render(<Shop />); await loaded(); await user.selectOptions(screen.getByRole("combobox", { name: word("storeFilter") }), auditStore.id); await user.selectOptions(screen.getByRole("combobox", { name: word("pageSize") }), "10"); vi.mocked(tenantAuditApi.read).mockResolvedValueOnce([auditRows[0]]); await user.click(button("apply")); await loaded(); expect(tenantAuditApi.read).toHaveBeenLastCalledWith({ ...defaultAuditQuery, storeId: auditStore.id, pageSize: 10 }, expect.any(AbortSignal));
});
it("paginates with the server’s exact page size, handles an empty next page and restores earlier pages", async () => {
  rows = rows.slice(0, 2); const user = userEvent.setup(); render(<Shop search="?pageSize=2" />); await loaded(); await user.click(button("next")); await screen.findByText(word("emptyPage")); expect(button("next").disabled).toBe(true); expect(screen.getByTestId("location").textContent).toBe("/audit?page=2&pageSize=2"); await user.click(button("previous")); await loaded(); expect(button("first").disabled).toBe(true); expect(tenantAuditApi.read).toHaveBeenLastCalledWith({ ...defaultAuditQuery, pageSize: 2 }, expect.any(AbortSignal));
});
it("enforces the offset/binder page bound without an invented last-page total", async () => {
  const size = 1, page = maxAuditPage(size); vi.mocked(tenantAuditApi.read).mockResolvedValueOnce([auditRows[0]]); render(<Shop search={`?pageSize=${size}&page=${page}`} />); await loaded(); expect(button("next").disabled).toBe(true); expect(screen.getByText(word("pageBound"))).toBeTruthy(); expect(screen.getByText(word("pagingHint"))).toBeTruthy();
});
it("clears all filters and paging on explicit reset, including an unapplied draft", async () => {
  const user = userEvent.setup(); render(<Shop search="?action=order.refunded&pageSize=2&page=2" />); await screen.findByText("<literal subject>"); await user.type(field("from"), "unapplied"); await user.click(button("clear")); await loaded(); expect(screen.getByTestId("location").textContent).toBe("/audit"); expect(field("from").value).toBe(""); expect(field("actionFilter").value).toBe(""); expect(tenantAuditApi.read).toHaveBeenLastCalledWith(defaultAuditQuery, expect.any(AbortSignal));
});
it("restores applied controls and results with browser Back and Forward", async () => {
  const user = userEvent.setup(); render(<Shop />); await loaded(); await user.type(field("actionFilter"), "future.action"); await user.click(button("apply")); await waitFor(() => expect(tenantAuditApi.read).toHaveBeenCalledTimes(2)); await user.click(screen.getByRole("button", { name: "Back in history" })); await waitFor(() => expect(tenantAuditApi.read).toHaveBeenCalledTimes(3)); expect(field("actionFilter").value).toBe(""); await user.click(screen.getByRole("button", { name: "Forward in history" })); await waitFor(() => expect(tenantAuditApi.read).toHaveBeenCalledTimes(4)); expect(field("actionFilter").value).toBe("future.action");
});
it.each([401, 403])("hides old records and filters after a refresh refusal %s", async code => {
  const user = userEvent.setup(); render(<Shop />); await loaded(); vi.mocked(tenantAuditApi.read).mockRejectedValueOnce(new HttpError("http", code)); await user.click(button("refresh")); await screen.findByText(word(code === 401 ? "expired" : "forbidden")); expect(screen.queryByRole("article")).toBeNull(); expect(screen.queryByRole("form")).toBeNull(); expect(screen.getByRole("link", { name: word("reload") }).getAttribute("href")).toMatch(/\/audit$/);
});
it.each([400, 429, 503])("has explicit GET retry after %s without stale records or an automatic retry", async code => {
  const user = userEvent.setup(); render(<Shop />); await loaded(); vi.mocked(tenantAuditApi.read).mockRejectedValueOnce(new HttpError("http", code)); await user.click(button("refresh")); await screen.findByRole("alert"); expect(screen.queryByRole("article")).toBeNull(); expect(tenantAuditApi.read).toHaveBeenCalledTimes(2); await user.click(screen.getByRole("button", { name: i18n.t("common:retry") })); await loaded(); expect(tenantAuditApi.read).toHaveBeenCalledTimes(3);
});
it.each(["id", "tenantId", "role", "email"] as const)("does not request audit data when the preflight %s changes", async field => {
  current = { ...current, [field]: "changed" }; render(<Shop />); await screen.findByText(word("changed")); expect(tenantAuditApi.read).not.toHaveBeenCalled(); expect(screen.queryByRole("form")).toBeNull();
});
it("discards returned records after a changed follow-up session", async () => {
  vi.mocked(tenantAuditApi.read).mockImplementationOnce(async () => { current.tenantId = "another-company"; return rows; }); render(<Shop />); await screen.findByText(word("changed")); expect(screen.queryByText(auditRows[0].subject)).toBeNull();
});
it("cancels a previous query and ignores its late records while a new query completes", async () => {
  const held = deferred<AuditRecord[]>(); vi.mocked(tenantAuditApi.read).mockReturnValueOnce(held.promise).mockResolvedValueOnce([]); const user = userEvent.setup(); render(<Shop />); await waitFor(() => expect(tenantAuditApi.read).toHaveBeenCalledTimes(1)); await user.type(field("actionFilter"), "no.matches"); await user.click(button("apply")); await screen.findByText(word("empty")); expect(vi.mocked(tenantAuditApi.read).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(auditRows)); expect(screen.queryByText(auditRows[0].subject)).toBeNull(); expect(tenantAuditApi.session).toHaveBeenCalledTimes(3);
});
it("aborts a later refresh on departure and skips the follow-up session read", async () => {
  const held = deferred<AuditRecord[]>(); const user = userEvent.setup(); render(<Shop />); await loaded(); vi.mocked(tenantAuditApi.read).mockReturnValueOnce(held.promise); await user.click(button("refresh")); await waitFor(() => expect(tenantAuditApi.read).toHaveBeenCalledTimes(2)); await user.click(screen.getByRole("link", { name: "Leave activity" })); expect(vi.mocked(tenantAuditApi.read).mock.calls[1][1].aborted).toBe(true); await act(async () => held.resolve(auditRows)); expect(tenantAuditApi.session).toHaveBeenCalledTimes(3); expect(screen.queryByText(auditRows[0].subject)).toBeNull();
});
it("drops old-company records and pending work when the rendered caller scope changes", async () => {
  const held = deferred<AuditRecord[]>(); vi.mocked(tenantAuditApi.read).mockReturnValueOnce(held.promise); const page = render(<Shop />); await waitFor(() => expect(tenantAuditApi.read).toHaveBeenCalledTimes(1)); current = { ...current, tenantId: "company-b" }; rows = []; page.rerender(<Shop user={current} />); await screen.findByText(word("empty")); await act(async () => held.resolve(auditRows)); expect(screen.queryByText(auditRows[0].subject)).toBeNull(); expect(vi.mocked(tenantAuditApi.read).mock.calls[0][1].aborted).toBe(true);
});
it("preserves an unapplied draft and literal records during language changes without refetching", async () => {
  const user = userEvent.setup(); render(<Shop />); await loaded(); await user.type(field("actionFilter"), "unfinished.action"); await act(async () => { await i18n.changeLanguage("cs"); }); expect(field("actionFilter").value).toBe("unfinished.action"); expect(screen.getByText(auditRows[0].subject)).toBeTruthy(); expect(tenantAuditApi.read).toHaveBeenCalledTimes(1);
});
it("completes initial checked reads under StrictMode", async () => { render(<StrictMode><Shop /></StrictMode>); await loaded(); });
