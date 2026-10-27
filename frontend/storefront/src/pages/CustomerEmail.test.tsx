// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import * as account from "../account";
import type { Customer } from "../account";
import { customerEmailApi } from "../api/customerEmail";
import { HttpError } from "../api/http";
import { captureEmailChangeEntry, type EmailChangeEntry } from "../emailChangeEntry";
import { CustomerProvider } from "../components/CustomerProvider";
import { useCustomer } from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import { StoreContext } from "../storeContext";
import type { Store } from "../store";
import { AccountEmailPage } from "./AccountEmailPage";
import { ConfirmEmailPage } from "./ConfirmEmailPage";
import { ResendVerificationPage } from "./ResendVerificationPage";
const store: Store = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", logoUrl: null, providerKeys: [], theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 } };
const original: Customer = { email: "ada@example.test", firstName: "Ada", lastName: "Lovelace", phone: null };
const changed: Customer = { ...original, email: "new@example.test" };
let actual: Customer | null;
function current() { if (actual) return structuredClone(actual); throw new HttpError("http", 401); }
function entry() { window.history.replaceState(null, "", "/account/confirm-email?token=fixture-proof-token&returnTo=https://foreign.test"); return captureEmailChangeEntry(); }
function Probe() { const session = useCustomer(); return <p data-testid="session">{session.status}:{session.customer?.email ?? "none"}</p>; }
function Shop({ path, captured, storeId = store.id }: { path: string; captured?: EmailChangeEntry | null; storeId?: string }) {
  return <StoreContext value={{ ...store, id: storeId }}><CustomerProvider><MemoryRouter initialEntries={[path]}><Probe /><Routes>
    <Route path="/account/email" element={<AccountEmailPage />} /><Route path="/account/confirm-email" element={<ConfirmEmailPage entry={captured ?? null} />} />
    <Route path="/account/resend-verification" element={<ResendVerificationPage />} /><Route path="/account" element={<h1>Account destination</h1>} />
  </Routes></MemoryRouter></CustomerProvider></StoreContext>;
}
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); actual = structuredClone(original);
  vi.spyOn(account, "getProfile").mockImplementation(async () => current());
  vi.spyOn(customerEmailApi, "profile").mockImplementation(async () => current());
  vi.spyOn(customerEmailApi, "resend").mockResolvedValue(); vi.spyOn(customerEmailApi, "request").mockResolvedValue();
  vi.spyOn(customerEmailApi, "logout").mockImplementation(async () => { actual = null; });
  vi.spyOn(customerEmailApi, "confirm").mockImplementation(async () => { actual = structuredClone(changed); return structuredClone(changed); });
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.history.replaceState(null,"","/"); window.sessionStorage.clear(); await i18n.changeLanguage("en"); });
async function fillChange(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole("button", { name: "Request email change" });
  await user.type(screen.getByRole("textbox", { name: "New email address" }), "  new@example.test  ");
  await user.type(screen.getByLabelText("Current password"), " raw password ");
}
it("resends only on explicit valid submission with generic acceptance and no sign-in", async () => {
  const user = userEvent.setup(); actual = null; render(<Shop path="/account/resend-verification?returnTo=%2Fcheckout" />);
  await user.click(screen.getByRole("button", { name: "Request verification email" })); expect(customerEmailApi.resend).not.toHaveBeenCalled();
  await user.type(screen.getByRole("textbox", { name: "E-mail" }), " ada@example.test "); await user.click(screen.getByRole("button", { name: "Request verification email" }));
  await screen.findByText(/If this store has a pending registration/); expect(customerEmailApi.resend).toHaveBeenCalledWith("ada@example.test", expect.any(AbortSignal));
  expect(screen.getByTestId("session").textContent).toBe("guest:none"); expect(window.sessionStorage.getItem("shopforge.auth.returnTo")).toBe("/checkout");
});
it("locks an uncertain resend without sending another message or inventing a delivery status", async () => {
  const user = userEvent.setup(); vi.mocked(customerEmailApi.resend).mockRejectedValue(new HttpError("network", null)); render(<Shop path="/account/resend-verification" />);
  await user.type(screen.getByRole("textbox", { name: "E-mail" }), "ada@example.test"); await user.click(screen.getByRole("button", { name: "Request verification email" })); await screen.findByText(/This page will not send it again/);
  fireEvent.submit(document.querySelector('form')!); expect(customerEmailApi.resend).toHaveBeenCalledTimes(1); expect(screen.getByRole("textbox")).toHaveProperty("value", "ada@example.test"); expect(screen.getByRole("textbox")).toHaveProperty("disabled", true);
});
it.each([400,429])("keeps a known resend refusal %s explicitly retryable", async status => {
  const user = userEvent.setup(); vi.mocked(customerEmailApi.resend).mockRejectedValue(new HttpError("http", status)); render(<Shop path="/account/resend-verification" />);
  await user.type(screen.getByRole("textbox", { name: "E-mail" }), "ada@example.test"); await user.click(screen.getByRole("button", { name: "Request verification email" })); await screen.findByRole("alert");
  expect(screen.getByRole("button", { name: "Request verification email" })).toHaveProperty("disabled", false); expect(customerEmailApi.resend).toHaveBeenCalledTimes(1);
});
it("requests a change with a fresh session and raw current password, retaining the current identity after acceptance", async () => {
  const user = userEvent.setup(); render(<Shop path="/account/email" />); await fillChange(user); await user.click(screen.getByRole("button", { name: "Request email change" }));
  await screen.findByText(/Your request was accepted/); expect(customerEmailApi.profile).toHaveBeenCalledTimes(1);
  expect(customerEmailApi.request).toHaveBeenCalledWith("new@example.test", " raw password ", expect.any(AbortSignal)); expect(screen.getByTestId("session").textContent).toBe("authenticated:ada@example.test");
  expect(customerEmailApi.confirm).not.toHaveBeenCalled(); expect(window.localStorage.getItem("email")).toBeNull();
});
it("retains the new address but clears and hides the password after a 403 refusal", async () => {
  const user = userEvent.setup(); vi.mocked(customerEmailApi.request).mockRejectedValue(new HttpError("http",403)); render(<Shop path="/account/email" />); await fillChange(user);
  await user.click(screen.getByRole("button", { name: "Show password" })); await user.click(screen.getByRole("button", { name: "Request email change" })); await screen.findByText(/The current password was not accepted/);
  expect(screen.getByLabelText("Current password")).toHaveProperty("value", ""); expect(screen.getByLabelText("Current password")).toHaveProperty("type", "password");
  expect(screen.getByRole("textbox", { name: "New email address" })).toHaveProperty("value", "new@example.test");
});
it("links newEmail field refusals to the retained new-address input", async () => {
  const user = userEvent.setup(); vi.mocked(customerEmailApi.request).mockRejectedValue(new HttpError("http",400,{errors:{newEmail:[]}})); render(<Shop path="/account/email" />); await fillChange(user); await user.click(screen.getByRole("button", { name: "Request email change" }));
  await user.click(await screen.findByRole("button", { name: i18n.t("validation:emailInvalid") })); expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "New email address" }));
});
it("does not submit with an observed different account, and clears the old credential/draft", async () => {
  const user = userEvent.setup(); render(<Shop path="/account/email" />); await fillChange(user); actual = { ...original, email: "other@example.test" };
  await user.click(screen.getByRole("button", { name: "Request email change" })); await screen.findByText(/A different account is signed in now/);
  expect(customerEmailApi.request).not.toHaveBeenCalled(); expect(screen.getByTestId("session").textContent).toBe("authenticated:other@example.test"); expect(screen.getByRole("textbox", { name: "New email address" })).toHaveProperty("value", "");
});
it("treats a failed preflight read as no submitted mutation and keeps a retry possible with a freshly typed password", async () => {
  const user = userEvent.setup(); vi.mocked(customerEmailApi.profile).mockRejectedValue(new HttpError("network",null)); render(<Shop path="/account/email" />); await fillChange(user); await user.click(screen.getByRole("button", { name: "Request email change" }));
  await screen.findByText(/No email confirmation or change request was submitted/); expect(customerEmailApi.request).not.toHaveBeenCalled(); expect(screen.getByLabelText("Current password")).toHaveProperty("value", ""); expect(screen.getByRole("button",{name:"Request email change"})).toHaveProperty("disabled", false);
});
it("handles a lost change-request answer without offering another POST on the current page", async () => {
  const user = userEvent.setup(); vi.mocked(customerEmailApi.request).mockRejectedValue(new HttpError("network",null)); render(<Shop path="/account/email" />); await fillChange(user); await user.click(screen.getByRole("button", { name: "Request email change" })); await screen.findByText(/this page will not repeat the request/);
  fireEvent.submit(document.querySelector('form')!); expect(customerEmailApi.request).toHaveBeenCalledTimes(1); expect(screen.getByLabelText("Current password")).toHaveProperty("value", "");
});
it("routes an expired session to an explicit sign-in link without submitting the change", async () => {
  const user = userEvent.setup(); render(<Shop path="/account/email" />); await fillChange(user); actual = null; await user.click(screen.getByRole("button",{name:"Request email change"}));
  expect(await screen.findByRole("link",{name:"Sign in"})).toHaveProperty("href","http://localhost:3000/account/sign-in?returnTo=%2Faccount%2Femail"); expect(customerEmailApi.request).not.toHaveBeenCalled();
});
it("requires explicit logout and a confirmed guest read before using a proof link, then applies the returned customer", async () => {
  const user = userEvent.setup(), captured = entry(); render(<Shop path="/account/confirm-email" captured={captured} />);
  await user.click(await screen.findByRole("button", { name: "Sign out to continue" })); expect(customerEmailApi.confirm).not.toHaveBeenCalled(); await user.click(await screen.findByRole("button",{name:"Confirm email change"}));
  await screen.findByText("Your email address is confirmed. This browser is signed in as new@example.test."); expect(customerEmailApi.confirm).toHaveBeenCalledWith("fixture-proof-token",expect.any(AbortSignal)); expect(captured!.available()).toBe(false); expect(screen.getByTestId("session").textContent).toBe("authenticated:new@example.test"); expect(location.search).toBe("");
});
it("detects another tab's sign-in before confirmation without consuming the token", async () => {
  const user = userEvent.setup(); actual = null; const captured = entry(); render(<Shop path="/account/confirm-email" captured={captured} />); await screen.findByRole("button",{name:"Confirm email change"}); actual = original;
  await user.click(screen.getByRole("button",{name:"Confirm email change"})); await screen.findByRole("button",{name:"Sign out to continue"}); expect(customerEmailApi.confirm).not.toHaveBeenCalled(); expect(captured!.available()).toBe(true);
});
it.each([400,409])("presents a terminal confirmation status %s without replaying the proof or guessing a profile", async status => {
  const user = userEvent.setup(); actual=null; vi.mocked(customerEmailApi.confirm).mockRejectedValue(new HttpError("http",status)); render(<Shop path="/account/confirm-email" captured={entry()} />); await user.click(await screen.findByRole("button",{name:"Confirm email change"}));
  await screen.findByRole("heading",{name:status===400?"This confirmation link is unavailable":"The email change could not be completed"}); expect(customerEmailApi.confirm).toHaveBeenCalledTimes(1); expect(screen.queryByRole("button",{name:"Confirm email change"})).toBeNull();
});
it("reconciles only the current sign-in after an applied confirmation loses its answer, without claiming success or replaying", async () => {
  const user=userEvent.setup(); actual=null; vi.mocked(customerEmailApi.confirm).mockImplementation(async()=>{actual=changed;throw new HttpError("network",null);}); render(<Shop path="/account/confirm-email" captured={entry()} />); await user.click(await screen.findByRole("button",{name:"Confirm email change"})); await screen.findByText(/This page will not use the link again/);
  await user.click(screen.getByRole("button",{name:"Check current sign-in"})); await screen.findByText("Currently signed in as new@example.test."); expect(screen.getByText(/It does not prove that the email change succeeded/)).toBeTruthy(); expect(customerEmailApi.confirm).toHaveBeenCalledTimes(1); expect(screen.queryByRole("heading",{name:"Email address changed"})).toBeNull();
});
it("does not repeat a logout when the following session read fails", async () => {
  const user=userEvent.setup(); vi.mocked(customerEmailApi.profile).mockRejectedValueOnce(new HttpError("network",null)).mockImplementationOnce(async()=>current()); render(<Shop path="/account/confirm-email" captured={entry()} />);
  await user.click(await screen.findByRole("button",{name:"Sign out to continue"})); await user.click(await screen.findByRole("button",{name:"Check current sign-in"})); await screen.findByRole("button",{name:"Confirm email change"}); expect(customerEmailApi.logout).toHaveBeenCalledTimes(1); expect(customerEmailApi.confirm).not.toHaveBeenCalled();
});
it("does not act on a missing/used memory entry and remains safe after leaving and remounting the page", async () => {
  const captured=entry(); captured!.take(); render(<Shop path="/account/confirm-email" captured={captured} />); await screen.findByRole("heading",{name:"This confirmation link is unavailable"}); expect(customerEmailApi.confirm).not.toHaveBeenCalled(); expect(customerEmailApi.profile).not.toHaveBeenCalled();
});
it("locks repeated confirmation clicks, aborts departure and discards its late identity answer", async () => {
  actual=null; let resolve!:(customer:Customer)=>void; vi.mocked(customerEmailApi.confirm).mockImplementation(()=>new Promise(done=>{resolve=done;})); render(<Shop path="/account/confirm-email" captured={entry()} />); const button=await screen.findByRole("button",{name:"Confirm email change"}); fireEvent.click(button); fireEvent.click(button); await waitFor(()=>expect(customerEmailApi.confirm).toHaveBeenCalledTimes(1));
  const signal=vi.mocked(customerEmailApi.confirm).mock.calls[0][1]; cleanup(); expect(signal.aborted).toBe(true); await act(async()=>resolve(changed)); expect(screen.queryByText(/Your email address is confirmed/)).toBeNull();
});
it("clears a departed store's email draft, aborts its pending write and ignores the late outcome", async () => {
  const user=userEvent.setup(); let resolve!:()=>void; vi.mocked(customerEmailApi.request).mockImplementation(()=>new Promise(done=>{resolve=done;}));
  function SwitchingShop(){const [id,setId]=useState(store.id);return <><button onClick={()=>setId('store-b')}>change store</button><Shop path="/account/email" storeId={id}/></>;}
  render(<SwitchingShop/>); await fillChange(user); await user.click(screen.getByRole("button",{name:"Request email change"})); await waitFor(()=>expect(customerEmailApi.request).toHaveBeenCalledTimes(1)); const signal=vi.mocked(customerEmailApi.request).mock.calls[0][2]; await user.click(screen.getByRole("button",{name:"change store"})); expect(signal.aborted).toBe(true); await act(async()=>resolve()); expect(screen.queryByText(/Your request was accepted/)).toBeNull(); expect(screen.getByRole("textbox",{name:"New email address"})).toHaveProperty("value","");
});
