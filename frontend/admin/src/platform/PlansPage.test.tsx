// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { HttpError } from "../api/http";
import { platformApi, type PlatformUser } from "../api/platform";
import { platformPlansApi, type PlatformPlan } from "../api/platformPlans";
import { i18n, initializeI18n } from "../i18n";
import english from "../i18n/locales/en";
import { plansOperator, platformPlan, newPlatformPlan } from "../test/platformPlanFixtures";
import { PlansPage } from "./PlansPage";

type Word = Exclude<keyof typeof english.resources.platformPlans, "invalid">;
const words = (key: Word) => i18n.t(`platformPlans:${key}`);
const button = (key: Word) => screen.getByRole("button", { name: words(key) });
const field = (key: "code" | "name" | "maxStores" | "maxProducts") => screen.getByLabelText(words(key), { exact: true }) as HTMLInputElement;
let plans: PlatformPlan[], operator: PlatformUser;
function Shop({ user = plansOperator, stale = false }: { user?: PlatformUser; stale?: boolean }) { return <PlansPage user={user} sessionStale={stale} />; }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
async function createForm() {
  const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("newPlan") }));
  for (const [key, value] of [["code", " Growth "], ["name", " Growth "], ["maxStores", "5"], ["maxProducts", ""]] as const) fireEvent.change(field(key), { target: { value } });
  return user;
}
async function editForm() {
  const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: "Edit Scale (scale)" }));
  fireEvent.change(field("name"), { target: { value: "Scale revised" } }); fireEvent.change(field("maxStores"), { target: { value: "0" } }); fireEvent.change(field("maxProducts"), { target: { value: "" } }); return user;
}
async function reviewCreate() { const user = await createForm(); await user.click(button("review")); await screen.findByRole("region", { name: words("reviewTitle") }); return user; }
async function reviewEdit() { const user = await editForm(); await user.click(button("review")); await screen.findByRole("region", { name: words("reviewTitle") }); return user; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); plans = [{ ...platformPlan }]; operator = { ...plansOperator };
  vi.spyOn(platformPlansApi, "session").mockImplementation(async () => ({ ...operator })); vi.spyOn(platformPlansApi, "list").mockImplementation(async () => structuredClone(plans));
  vi.spyOn(platformPlansApi, "create").mockImplementation(async values => { const saved = { ...newPlatformPlan, ...values }; plans.push(saved); return saved; });
  vi.spyOn(platformPlansApi, "update").mockImplementation(async (original, values) => { const saved = { ...original, ...values }; plans = plans.map(plan => plan.id === original.id ? saved : plan); return saved; });
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });
it("lists real zero/unlimited/default terms after checking the operator before and after the read", async () => {
  render(<Shop />); await screen.findByRole("heading", { name: "Scale" }); expect(screen.getByText(words("uncapped"))).toBeTruthy(); expect(screen.getByText("0")).toBeTruthy(); expect(screen.getByText(words("defaultYes"))).toBeTruthy(); expect(platformPlansApi.session).toHaveBeenCalledTimes(2); expect(platformPlansApi.create).not.toHaveBeenCalled(); expect(platformPlansApi.update).not.toHaveBeenCalled(); expect(screen.queryByRole("button", { name: /delete|default/i })).toBeNull();
});
it("opens the actual operator route and navigation without company, tenant or staff reads", async () => {
  vi.spyOn(platformApi, "me").mockResolvedValue(plansOperator); const staff = vi.spyOn(api, "me"), stores = vi.spyOn(api, "stores"), tenants = vi.spyOn(platformApi, "tenants"); history.replaceState(null, "", "/platform/plans"); render(<App />);
  await screen.findByRole("heading", { name: "Scale" }); expect(screen.getByRole("link", { name: words("title") }).getAttribute("aria-current")).toBe("page"); expect(document.title).toBe("Platform plans · ShopForge Platform"); expect(staff).not.toHaveBeenCalled(); expect(stores).not.toHaveBeenCalled(); expect(tenants).not.toHaveBeenCalled();
});
it.each([true, false])("shows empty or missing-default catalogues without fabricating a plan: empty %s", async empty => {
  plans = empty ? [] : [newPlatformPlan]; render(<Shop />); await screen.findByText(words(empty ? "empty" : "noDefault")); expect(platformPlansApi.create).not.toHaveBeenCalled();
});
it("validates and focuses fields before any review or write", async () => {
  render(<Shop />); const user = await createForm(); expect(field("code").required).toBe(true); fireEvent.change(field("code"), { target: { value: " " } }); fireEvent.change(field("name"), { target: { value: " " } }); fireEvent.change(field("maxStores"), { target: { value: "1e3" } }); await user.click(button("review"));
  for (const key of ["code", "name", "maxStores"] as const) { await user.click(screen.getByRole("button", { name: i18n.t(`platformPlans:invalid.${key}`) })); expect(document.activeElement).toBe(field(key)); }
  expect(platformPlansApi.list).toHaveBeenCalledTimes(1); expect(platformPlansApi.create).not.toHaveBeenCalled();
});
it("reviews exact normalized new terms before creating and refreshes the confirmed record", async () => {
  render(<Shop />); const user = await reviewCreate(); const review = screen.getByRole("region", { name: words("reviewTitle") }); expect(review).toBe(document.activeElement); expect(within(review).getByText("growth")).toBeTruthy(); expect(platformPlansApi.create).not.toHaveBeenCalled();
  await user.click(button("confirmCreate")); await screen.findByText(words("saved")); expect(platformPlansApi.create).toHaveBeenCalledExactlyOnceWith({ code: "growth", name: "Growth", maxStores: 5, maxProducts: null }, expect.any(AbortSignal)); expect(platformPlansApi.list).toHaveBeenCalledTimes(3); expect(platformPlansApi.session).toHaveBeenCalledTimes(6); await user.click(button("finishReview")); await screen.findByRole("heading", { name: "Growth" });
});
it("names the shared/default impact and requires review before updating immutable saved identity", async () => {
  render(<Shop />); const user = await reviewEdit(); expect(screen.getByText(words("sharedImpact"))).toBeTruthy(); expect(screen.getByText(words("defaultImpact"))).toBeTruthy(); expect(screen.queryByLabelText(words("code"))).toBeNull(); expect(platformPlansApi.update).not.toHaveBeenCalled();
  await user.click(button("confirmEdit")); await screen.findByText(words("saved")); expect(platformPlansApi.update).toHaveBeenCalledExactlyOnceWith(platformPlan, { code: "scale", name: "Scale revised", maxStores: 0, maxProducts: null }, expect.any(AbortSignal));
});
it("does not send an unchanged edit and restores focus after review Escape and cancel", async () => {
  render(<Shop />); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: "Edit Scale (scale)" })); await user.click(button("review")); await screen.findByText(words("noChanges")); expect(platformPlansApi.update).not.toHaveBeenCalled();
  fireEvent.change(field("name"), { target: { value: "New" } }); await user.click(button("review")); await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(field("name"))); await user.keyboard("{Escape}"); await screen.findByRole("heading", { name: "Scale" }); await waitFor(() => expect(document.activeElement).toBe(button("newPlan")));
});
it("refuses a code that appeared after the create review without posting", async () => {
  render(<Shop />); const user = await reviewCreate(); plans.push(newPlatformPlan); await user.click(button("confirmCreate")); await screen.findByText(words("duplicate")); expect(field("code").value).toBe(" Growth "); expect(platformPlansApi.create).not.toHaveBeenCalled();
});
it.each(["name", "code", "maxStores", "maxProducts", "isDefault"] as const)("preserves draft and blocks a changed saved %s until explicit reload", async key => {
  render(<Shop />); const user = await reviewEdit(); plans = [{ ...platformPlan, [key]: key === "isDefault" ? false : key.startsWith("max") ? 4 : "Other" }]; await user.click(button("confirmEdit")); await screen.findByText(words("recordChanged")); expect(field("name").value).toBe("Scale revised"); expect(field("name").readOnly).toBe(true); expect(platformPlansApi.update).not.toHaveBeenCalled();
  await user.click(button("reloadSaved")); expect(field("name").value).toBe(plans[0].name); expect(field("name").readOnly).toBe(false); await waitFor(() => expect(document.activeElement).toBe(field("name")));
});
it("closes a missing plan review and retains a selectable draft without sending PUT", async () => {
  render(<Shop />); const user = await reviewEdit(); plans = []; await user.click(button("confirmEdit")); await screen.findByText(words("missing")); expect(field("name").value).toBe("Scale revised"); expect(button("review")).toHaveProperty("disabled", true); expect(platformPlansApi.update).not.toHaveBeenCalled();
});
it.each([400, 409, 429])("keeps a known create refusal %s explicit and preserves the draft for fresh review", async code => {
  vi.mocked(platformPlansApi.create).mockRejectedValueOnce(new HttpError("http", code, code === 400 ? { errors: { maxStores: ["private"] } } : null)); render(<Shop />); const user = await reviewCreate(); await user.click(button("confirmCreate")); await screen.findByText(words(code === 400 ? "refused" : code === 409 ? "duplicate" : "limited")); expect(field("name").value).toBe(" Growth "); expect(platformPlansApi.create).toHaveBeenCalledTimes(1); expect(document.body.innerHTML).not.toContain("private");
  await user.click(button("review")); await user.click(button("confirmCreate")); await screen.findByText(words("saved")); expect(platformPlansApi.create).toHaveBeenCalledTimes(2);
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 201)])("never resends an unknown create, even when matching terms appear: %j", async error => {
  vi.mocked(platformPlansApi.create).mockImplementationOnce(async () => { plans.push(newPlatformPlan); throw error; }); render(<Shop />); const user = await reviewCreate(); await user.click(button("confirmCreate")); await screen.findByText(words("uncertain")); expect(field("name").disabled).toBe(true); expect(button("finishReview")).toHaveProperty("disabled", true);
  await user.click(button("checkCatalogue")); await screen.findByText(words("checkedUncertain")); expect(field("name").disabled).toBe(true); expect(platformPlansApi.create).toHaveBeenCalledTimes(1); expect(screen.queryByText(words("saved"))).toBeNull(); await user.click(button("finishReview")); await screen.findByRole("heading", { name: "Growth" });
});
it("keeps a confirmed save distinct from a failed follow-up and recovers by GET only", async () => {
  vi.mocked(platformPlansApi.list).mockResolvedValueOnce([platformPlan]).mockResolvedValueOnce([platformPlan]).mockRejectedValueOnce(new HttpError("network", null)); render(<Shop />); const user = await reviewCreate(); await user.click(button("confirmCreate")); await screen.findByText(words("savedReadFailed")); expect(field("name").disabled).toBe(true); await user.click(button("checkCatalogue")); await screen.findByText(words("saved")); expect(platformPlansApi.create).toHaveBeenCalledTimes(1);
});
it("labels an acknowledged save whose subsequent catalogue has different terms", async () => {
  vi.mocked(platformPlansApi.create).mockImplementationOnce(async () => { plans.push({ ...newPlatformPlan, name: "Changed elsewhere" }); return newPlatformPlan; }); render(<Shop />); const user = await reviewCreate(); await user.click(button("confirmCreate")); await screen.findByText(words("changedAfterSave")); expect(screen.getByRole("heading", { name: words("latestTerms") })).toBeTruthy(); expect(platformPlansApi.create).toHaveBeenCalledTimes(1);
});
it("retries only a failed preflight read and preserves the reviewed intent", async () => {
  render(<Shop />); const user = await reviewCreate(); vi.mocked(platformPlansApi.list).mockRejectedValueOnce(new HttpError("network", null)); await user.click(button("confirmCreate")); await screen.findByText(words("preflightFailed")); expect(platformPlansApi.create).not.toHaveBeenCalled();
  await user.click(button("refresh")); await waitFor(() => expect(button("confirmCreate")).toHaveProperty("disabled", false)); expect(platformPlansApi.create).not.toHaveBeenCalled();
});
it.each(["id", "email", "isTwoFactorEnabled"] as const)("refuses a preflight for changed operator %s", async key => {
  render(<Shop />); const user = await reviewCreate(); operator = { ...operator, [key]: key === "isTwoFactorEnabled" ? false : "other" }; await user.click(button("confirmCreate")); await screen.findByText(words("changed")); expect(screen.queryByRole("form")).toBeNull(); expect(platformPlansApi.create).not.toHaveBeenCalled();
});
it("discards save facts after a post-write operator change", async () => {
  vi.mocked(platformPlansApi.create).mockImplementationOnce(async () => { operator.email = "other@example.test"; return newPlatformPlan; }); render(<Shop />); const user = await reviewCreate(); await user.click(button("confirmCreate")); await screen.findByText(words("changed")); expect(screen.queryByText(words("saved"))).toBeNull(); expect(screen.queryByRole("form")).toBeNull();
});
it.each([401, 403])("closes controls on write denial %s without changing another auth scope", async code => {
  vi.mocked(platformPlansApi.create).mockRejectedValueOnce(new HttpError("http", code)); render(<Shop />); const user = await reviewCreate(); await user.click(button("confirmCreate")); await screen.findByText(words(code === 401 ? "expired" : "forbidden")); expect(screen.queryByRole("form")).toBeNull();
});
it.each([401, 403, 429, 503])("handles initial read refusal %s without creating a plan", async code => {
  vi.mocked(platformPlansApi.list).mockRejectedValueOnce(new HttpError("http", code)); render(<Shop />);
  if (code === 401 || code === 403) await screen.findByText(words(code === 401 ? "expired" : "forbidden")); else { await screen.findByRole("alert"); const user = userEvent.setup(); await user.click(button("refresh")); await screen.findByRole("heading", { name: "Scale" }); }
  expect(platformPlansApi.create).not.toHaveBeenCalled();
});
it("locks duplicate submissions and discards a late write after parent logout", async () => {
  const view = render(<Shop />); await reviewCreate(); const held = deferred<PlatformPlan>(); vi.mocked(platformPlansApi.create).mockReturnValueOnce(held.promise); const confirm = button("confirmCreate"); fireEvent.click(confirm); fireEvent.click(confirm); await waitFor(() => expect(platformPlansApi.create).toHaveBeenCalledTimes(1)); view.rerender(<Shop stale />); expect(vi.mocked(platformPlansApi.create).mock.calls[0][1].aborted).toBe(true); const reads = vi.mocked(platformPlansApi.session).mock.calls.length; await act(async () => held.resolve(newPlatformPlan)); expect(platformPlansApi.session).toHaveBeenCalledTimes(reads); expect(screen.queryByRole("form")).toBeNull();
});
it("starts no reads while the parent operator session is unresolved", () => { render(<Shop stale />); expect(platformPlansApi.session).not.toHaveBeenCalled(); expect(platformPlansApi.list).not.toHaveBeenCalled(); });
it("preserves draft and reviewed values on a locale change without API work", async () => {
  render(<Shop />); await reviewCreate(); const reads = vi.mocked(platformPlansApi.list).mock.calls.length; await act(async () => { await i18n.changeLanguage("cs"); }); expect(screen.getByRole("region", { name: words("reviewTitle") })).toBeTruthy(); expect(screen.getByText("growth")).toBeTruthy(); expect(platformPlansApi.list).toHaveBeenCalledTimes(reads); expect(platformPlansApi.create).not.toHaveBeenCalled();
});
it("supports StrictMode entry with reads only", async () => { render(<StrictMode><Shop /></StrictMode>); await screen.findByRole("heading", { name: "Scale" }); expect(platformPlansApi.create).not.toHaveBeenCalled(); });

it.each([400, 429])("preserves an update draft after known refusal %s and requires a new review", async code => {
  vi.mocked(platformPlansApi.update).mockRejectedValueOnce(new HttpError("http", code)); render(<Shop />); const user = await reviewEdit(); await user.click(button("confirmEdit")); await screen.findByText(words(code === 400 ? "refused" : "limited")); expect(field("name").value).toBe("Scale revised"); await user.click(button("review")); await user.click(button("confirmEdit")); await screen.findByText(words("saved")); expect(platformPlansApi.update).toHaveBeenCalledTimes(2);
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("http", 409)])("blocks an unknown update after GET reconciliation without replay: %j", async error => {
  vi.mocked(platformPlansApi.update).mockRejectedValueOnce(error); render(<Shop />); const user = await reviewEdit(); await user.click(button("confirmEdit")); await screen.findByText(words("uncertain")); await user.click(button("checkCatalogue")); await screen.findByText(words("checkedUncertain")); expect(platformPlansApi.update).toHaveBeenCalledTimes(1); expect(button("review")).toHaveProperty("disabled", true);
});
it("removes a plan refused as missing during PUT from the current catalogue", async () => {
  vi.mocked(platformPlansApi.update).mockRejectedValueOnce(new HttpError("http", 404)); render(<Shop />); const user = await reviewEdit(); await user.click(button("confirmEdit")); await screen.findByText(words("missing")); await user.click(button("cancel")); await screen.findByText(words("empty")); expect(platformPlansApi.update).toHaveBeenCalledTimes(1);
});
it("does not accept a newly created response that reuses an existing plan id", async () => {
  vi.mocked(platformPlansApi.create).mockResolvedValueOnce({ ...newPlatformPlan, id: platformPlan.id }); render(<Shop />); const user = await reviewCreate(); await user.click(button("confirmCreate")); await screen.findByText(words("uncertain")); expect(screen.queryByText(words("saved"))).toBeNull(); expect(platformPlansApi.create).toHaveBeenCalledTimes(1);
});
it("hides old records during refresh and after a failed fresh read", async () => {
  render(<Shop />); await screen.findByRole("heading", { name: "Scale" }); const held = deferred<PlatformPlan[]>(); vi.mocked(platformPlansApi.list).mockReturnValueOnce(held.promise); const user = userEvent.setup(); await user.click(button("refresh")); expect(screen.queryByRole("heading", { name: "Scale" })).toBeNull(); await act(async () => held.resolve([])); await screen.findByText(words("empty"));
  vi.mocked(platformPlansApi.list).mockRejectedValueOnce(new HttpError("network", null)); await user.click(button("refresh")); await screen.findByRole("alert"); expect(screen.queryByRole("button", { name: words("newPlan") })).toBeNull();
});
it("aborts a departing preflight without posting or keeping late catalogue facts", async () => {
  const view = render(<Shop />); await reviewCreate(); const held = deferred<PlatformUser>(); vi.mocked(platformPlansApi.session).mockReturnValueOnce(held.promise); fireEvent.click(button("confirmCreate")); const request = vi.mocked(platformPlansApi.session).mock.calls.at(-1)![0]; view.unmount(); expect(request.aborted).toBe(true); await act(async () => held.resolve(plansOperator)); expect(platformPlansApi.create).not.toHaveBeenCalled();
});
