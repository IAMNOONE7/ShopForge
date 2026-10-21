// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { HttpError } from "../api/http";
import { platformApi, type PlatformTenant } from "../api/platform";
import { i18n, initializeI18n } from "../i18n";
import { TenantsPage } from "./TenantsPage";

const company: PlatformTenant = { id: "00000000-0000-0000-0000-000000000001", name: "Company A", status: "Active", planCode: "basic", usage: [{ name: "stores", value: 7 }, { name: "future-count", value: 0 }] };
function app(userId = "operator", sessionStale = false) { return <TenantsPage key={userId} userId={userId} sessionStale={sessionStale} />; }
function deferred<T>() { let resolve!: (value: T) => void, reject!: (reason: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
async function open(user = userEvent.setup()) { await user.click(await screen.findByRole("button", { name: "Suspend access for Company A" })); return user; }
beforeAll(async () => { await initializeI18n(); });
beforeEach(async () => { await i18n.changeLanguage("en"); vi.spyOn(platformApi, "tenants").mockImplementation(async () => structuredClone([company])); vi.spyOn(platformApi, "changeTenant").mockResolvedValue({ ...company, status: "Suspended", usage: [] }); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("preserves actual plan, unknown counts and zero without inventing defaults or controls for future statuses", async () => {
  vi.mocked(platformApi.tenants).mockResolvedValue([company, { ...company, id: "00000000-0000-0000-0000-000000000002", name: "Unknown", status: "Future", planCode: null, usage: [] }]); render(app());
  await screen.findByRole("heading", { name: "Company A" }); fireEvent.click(screen.getAllByText("Recorded usage")[0]);
  expect(screen.getByText("basic")).toBeTruthy(); expect(screen.getByText("future-count").nextElementSibling?.textContent).toBe("0");
  expect(screen.getByText("Stores").nextElementSibling?.textContent).toBe("7"); expect(screen.getByText("No plan recorded")).toBeTruthy();
  expect(screen.getByText(/Unrecognized recorded status: Future/)).toBeTruthy(); expect(screen.queryByRole("button", { name: /access for Unknown/ })).toBeNull();
});
it("searches all loaded companies rather than the displayed page, and filters actual statuses locally", async () => {
  const many = Array.from({ length: 30 }, (_, index) => ({ ...company, id: `00000000-0000-0000-0000-${String(index).padStart(12, "0")}`, name: `Company ${index}`, status: index === 29 ? "Suspended" : "Active" }));
  vi.mocked(platformApi.tenants).mockResolvedValue(many); const user = userEvent.setup(); render(app()); await screen.findByRole("heading", { name: "Company 0" });
  expect(screen.queryByRole("heading", { name: "Company 29" })).toBeNull(); await user.click(screen.getByRole("button", { name: "Next" })); await screen.findByRole("heading", { name: "Company 29" });
  await user.type(screen.getByRole("textbox", { name: "Company name or ID" }), "Company 29"); await user.click(screen.getByRole("button", { name: "Search loaded companies" }));
  expect(screen.getAllByRole("heading", { level: 2 })).toHaveLength(1); expect(platformApi.tenants).toHaveBeenCalledOnce();
  await user.selectOptions(screen.getByRole("combobox", { name: "Company status" }), "Active"); expect(screen.getByText("No loaded company matches these filters.")).toBeTruthy();
});
it("distinguishes an empty list from a failed read and explicitly retries only the read", async () => {
  vi.mocked(platformApi.tenants).mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue([]); const user = userEvent.setup(); render(app());
  await user.click(await screen.findByRole("button", { name: "Try again" })); await screen.findByText("No companies were returned."); expect(platformApi.changeTenant).not.toHaveBeenCalled();
});
it("retains failed-refresh records visibly but blocks their actions", async () => {
  const user = userEvent.setup(); render(app()); const button = await screen.findByRole("button", { name: "Suspend access for Company A" });
  vi.mocked(platformApi.tenants).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(screen.getByRole("button", { name: "Refresh companies" }));
  await screen.findByRole("button", { name: "Try again" }); expect(button).toHaveProperty("disabled", true); expect(screen.getByRole("heading", { name: "Company A" })).toBeTruthy();
});
it("names the company and ID, focuses confirmation, cancels with Escape and returns focus without writing", async () => {
  render(app()); await open(); expect(screen.getByRole("heading", { name: "Suspend Company A?" })).toBe(document.activeElement);
  const section = screen.getByRole("region", { name: "Suspend Company A?" }); expect(within(section).getByText(company.id)).toBeTruthy();
  fireEvent.keyDown(document.activeElement!, { key: "Escape" }); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Suspend access for Company A" })));
  expect(platformApi.changeTenant).not.toHaveBeenCalled();
});
it.each(["suspend", "resume"] as const)("locks a duplicate %s and takes status and counts from the authoritative reload", async action => {
  const initial = { ...company, status: action === "suspend" ? "Active" : "Suspended" }, updated = { ...company, status: action === "suspend" ? "Suspended" : "Active" };
  vi.mocked(platformApi.tenants).mockResolvedValue([initial]); const held = deferred<PlatformTenant>(); vi.mocked(platformApi.changeTenant).mockReturnValue(held.promise);
  render(app()); fireEvent.click(await screen.findByRole("button", { name: action === "suspend" ? "Suspend access for Company A" : "Restore access for Company A" }));
  const confirm = screen.getByRole("button", { name: action === "suspend" ? "Confirm suspension" : "Confirm access restoration" }); fireEvent.click(confirm); fireEvent.click(confirm);
  expect(platformApi.changeTenant).toHaveBeenCalledExactlyOnceWith(company.id, action, expect.any(AbortSignal));
  vi.mocked(platformApi.tenants).mockResolvedValue([updated]); await act(async () => held.resolve({ ...updated, usage: [] }));
  await screen.findByText(/The server acknowledged/); await waitFor(() => expect(platformApi.tenants).toHaveBeenCalledTimes(2));
  fireEvent.click(screen.getByText("Recorded usage")); expect(screen.getByText("Stores").nextElementSibling?.textContent).toBe("7"); expect(screen.queryByText("No usage counts were returned.")).toBeNull();
});
it("keeps an acknowledged result when the next read fails, without enabling another write", async () => {
  const user = userEvent.setup(); render(app()); await open(user); vi.mocked(platformApi.tenants).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(screen.getByRole("button", { name: "Confirm suspension" })); await screen.findByText(/The server acknowledged/); await screen.findByRole("button", { name: "Try again" });
  expect(screen.getByRole("button", { name: "Suspend access for Company A" })).toHaveProperty("disabled", true); expect(platformApi.changeTenant).toHaveBeenCalledOnce();
});
it("retains a refused intent and blocks a stale confirmation after a concurrent status change", async () => {
  vi.mocked(platformApi.changeTenant).mockRejectedValue(new HttpError("http", 409)); const user = userEvent.setup(); render(app()); await open(user);
  vi.mocked(platformApi.tenants).mockResolvedValue([{ ...company, status: "Suspended" }]); await user.click(screen.getByRole("button", { name: "Confirm suspension" }));
  await screen.findByText(/The request for Company A was refused/); await screen.findByText(/The company name or status changed/);
  expect(screen.getByRole("button", { name: "Confirm suspension" })).toHaveProperty("disabled", true); expect(platformApi.changeTenant).toHaveBeenCalledOnce();
  await user.click(screen.getByRole("button", { name: "Cancel" })); expect(screen.getByRole("button", { name: "Restore access for Company A" })).toHaveProperty("disabled", false);
});
it.each([new HttpError("network", null), new HttpError("invalid-response", null), new HttpError("http", 503)])("requires a successful read and explicit review after an uncertain outcome %j", async error => {
  vi.mocked(platformApi.changeTenant).mockRejectedValue(error); const user = userEvent.setup(); render(app()); await open(user);
  vi.mocked(platformApi.tenants).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(screen.getByRole("button", { name: "Confirm suspension" }));
  const finish = await screen.findByRole("button", { name: "Finish review" }); await screen.findByRole("button", { name: "Try again" }); expect(finish).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(finish).toHaveProperty("disabled", false));
  expect(screen.getByRole("button", { name: "Suspend access for Company A" })).toHaveProperty("disabled", true); await user.click(finish);
  expect(screen.getByRole("button", { name: "Suspend access for Company A" })).toHaveProperty("disabled", false); expect(platformApi.changeTenant).toHaveBeenCalledOnce();
});
it("blocks confirmation for renamed/missing companies but permits independently updated usage", async () => {
  const user = userEvent.setup(); render(app()); await open(user); vi.mocked(platformApi.tenants).mockResolvedValue([{ ...company, usage: [{ name: "stores", value: 8 }] }]);
  await user.click(screen.getByRole("button", { name: "Refresh companies" })); await waitFor(() => expect(screen.getByRole("button", { name: "Confirm suspension" })).toHaveProperty("disabled", false));
  vi.mocked(platformApi.tenants).mockResolvedValue([{ ...company, name: "Renamed" }]); await user.click(screen.getByRole("button", { name: "Refresh companies" })); await screen.findByText(/The company name or status changed/);
  expect(screen.getByRole("button", { name: "Confirm suspension" })).toHaveProperty("disabled", true);
  vi.mocked(platformApi.tenants).mockResolvedValue([]); await user.click(screen.getByRole("button", { name: "Refresh companies" })); await screen.findByText("No companies were returned.");
  expect(screen.getByRole("button", { name: "Confirm suspension" })).toHaveProperty("disabled", true);
});
it("blocks an open intent while the session requires rechecking", async () => {
  const view = render(app()); await open(); view.rerender(app("operator", true));
  const confirm = screen.getByRole("button", { name: "Confirm suspension" }); expect(confirm).toHaveProperty("disabled", true); fireEvent.click(confirm); expect(platformApi.changeTenant).not.toHaveBeenCalled();
});
it("discards an old operator's draft and ignores late reads and writes on departure", async () => {
  const held = deferred<PlatformTenant>(); vi.mocked(platformApi.changeTenant).mockReturnValueOnce(held.promise); const view = render(app()); await open();
  fireEvent.click(screen.getByRole("button", { name: "Confirm suspension" })); const signal = vi.mocked(platformApi.changeTenant).mock.calls[0][2]!;
  view.rerender(app("another-operator")); await screen.findByRole("heading", { name: "Company A" }); expect(signal.aborted).toBe(true);
  const reads = vi.mocked(platformApi.tenants).mock.calls.length; await act(async () => held.resolve({ ...company, status: "Suspended" }));
  expect(platformApi.tenants).toHaveBeenCalledTimes(reads); expect(screen.queryByText(/The server acknowledged/)).toBeNull(); expect(screen.queryByRole("heading", { name: "Suspend Company A?" })).toBeNull();
});
