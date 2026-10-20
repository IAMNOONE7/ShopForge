import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api";
const method = { code: "kartou", name: "Platba kartou", providerKey: "comgate", isActive: false };
function reply(body: unknown, status = 200) { const fetch = vi.fn().mockImplementation(async () => status === 204 ? new Response(null, { status }) : Response.json(body, { status })); vi.stubGlobal("fetch", fetch); return fetch; }
afterEach(() => vi.unstubAllGlobals());
it("reads saved inactive and unoffered methods without inventing defaults, with encoded scope and cancellation", async () => {
  const fetch = reply([method, { ...method, code: "old", providerKey: "unoffered" }]), controller = new AbortController();
  expect(await api.paymentMethods("a/b", controller.signal)).toEqual([method, { ...method, code: "old", providerKey: "unoffered" }]); expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/payment-methods"); expect(fetch.mock.calls[0][1].signal).toBe(controller.signal);
});
it.each([null, {}, [null], [{ ...method, isActive: undefined }], [{ ...method, code: "" }], [{ ...method, providerKey: null }], [{ ...method, name: 5 }], [method, method]])("refuses incomplete method reads %j", async body => {
  reply(body); await expect(api.paymentMethods("a")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each(["payment", "shipping"] as const)("uses checked dynamic %s discovery and does not add Stripe or manual defaults", async kind => {
  const fetch = reply(["comgate"]); expect(await (kind === "payment" ? api.paymentProviders("a/b") : api.shippingProviders("a/b"))).toEqual(["comgate"]); expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/a%2Fb/${kind}-providers`);
});
it.each([null, {}, [""], [" comgate"], [null], ["comgate", "comgate"]])("refuses malformed provider lists %j", async body => {
  reply(body); await expect(api.paymentProviders("a")).rejects.toMatchObject({ kind: "invalid-response" });
});
it("keeps an empty offer empty and honors a canceled read", async () => {
  reply([]); expect(await api.paymentMethods("a")).toEqual([]); expect(await api.paymentProviders("a")).toEqual([]);
  const controller = new AbortController(); vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => { controller.abort(); return Response.json([method]); })); await expect(api.paymentMethods("a", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it("creates a named active method through the real POST and accepts the server-derived Czech-name code", async () => {
  const fetch = reply({ ...method, code: "lut-karta", name: "Žlutá karta", isActive: true });
  expect((await api.createPaymentMethod("a/b", "Žlutá karta", "comgate")).code).toBe("lut-karta"); expect(fetch.mock.calls[0][1].method).toBe("POST"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: "Žlutá karta", providerKey: "comgate", isActive: true });
});
it("updates only name and active state, preserving immutable code/provider", async () => {
  const fetch = reply({ ...method, code: "card/a", name: "Kartou" }); await api.updatePaymentMethod("a/b", "card/a", { name: "Kartou", isActive: false });
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/payment-methods/card%2Fa"); expect(fetch.mock.calls[0][1].method).toBe("PUT"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: "Kartou", isActive: false });
});
it.each([{ ...method, code: "other" }, { ...method, name: "Wrong" }, { ...method, isActive: true }, null])("treats a mismatched payment reply as uncertain: %j", async body => {
  const fetch = reply(body); await expect(api.updatePaymentMethod("a", method.code, { name: method.name, isActive: false })).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
});
it.each([400, 403, 404, 409, 503])("keeps server refusal %s without resending", async status => {
  const fetch = reply({ errors: { providerKey: ["Not offered"] } }, status); await expect(api.createPaymentMethod("a", "Card", "stripe")).rejects.toMatchObject({ status }); expect(fetch).toHaveBeenCalledOnce();
});
