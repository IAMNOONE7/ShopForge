import { afterEach, describe, expect, it, vi } from "vitest";
import { api, type AttributeInput, type AttributeUpdate } from "../api";
import { attributeDefinition as definition, textAttribute } from "../test/attributeFixtures";
import { changeAttribute } from "../components/attributeCommands";

function serve(value: unknown, status = 200) {
  const fetch = vi.fn().mockResolvedValue(status === 204 ? new Response(null, { status }) : Response.json(value, { status }));
  vi.stubGlobal("fetch", fetch); return fetch;
}
const settings: AttributeUpdate = { name: "Timber", unit: "cm", isFilterable: true, isVisibleOnProductPage: false, isInFeeds: false, isSearchable: false, sortOrder: -2 };
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("attribute lifecycle contracts", () => {
  it("reads one checked store collection, encodes the path and preserves all flags/codes in order", async () => {
    const fetch = serve([definition, textAttribute]); const signal = new AbortController().signal;
    expect(await api.attributes("store/a", signal)).toEqual([definition, textAttribute]);
    expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%2Fa/attributes");
    expect(fetch.mock.calls[0][1].signal).toBe(signal);
  });
  it.each([null, {}, [null], [definition, definition], [{ ...definition, isInFeeds: undefined }],
    [{ ...definition, isSearchable: true }], [{ ...textAttribute, isFilterable: true }], [{ ...textAttribute, options: definition.options }],
    [{ ...definition, type: "future" }], [{ ...definition, sortOrder: 2.5 }], [{ ...definition, options: null }],
    [{ ...definition, options: [definition.options[0], definition.options[0]] }],
    [{ ...definition, options: [{ ...definition.options[0], code: "Bad Code" }] }],
  ])("rejects an incomplete or ambiguous collection %j", async value => {
    serve(value); await expect(api.attributes("store")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("creates with explicit search/feed settings and saves the complete permitted settings only", async () => {
    const input: AttributeInput = { code: "care", name: "Care", type: "text", unit: null, isFilterable: false, isVisibleOnProductPage: true,
      sortOrder: 2, isInFeeds: true, isSearchable: true, options: [] };
    const fetch = serve(textAttribute);
    expect(await api.createAttribute("store", input)).toEqual(textAttribute);
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual(input);
    const updated = serve({ ...definition, ...settings });
    expect(await api.updateAttribute("store/a", definition.id, settings)).toEqual({ ...definition, ...settings });
    expect(JSON.parse(updated.mock.calls[0][1].body)).toEqual(settings);
    expect(updated.mock.calls[0][0]).toBe(`/api/admin/stores/store%2Fa/attributes/${definition.id}`);
  });
  it.each([{ ...definition, name: "Other" }, { ...definition, ...settings, isInFeeds: true }, { ...definition, ...settings, id: "other" }])("does not acknowledge a mismatched settings answer %j", async value => {
    serve(value); await expect(api.updateAttribute("store", definition.id, settings)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("renames only words and accepts a duplicate label or punctuation while retaining the code", async () => {
    const value = { ...definition, options: definition.options.map((option, index) => index === 0 ? { ...option, name: "Beech" } : option) };
    const fetch = serve(value); const signal = new AbortController().signal;
    await changeAttribute("store", definition, { kind: "rename", optionId: definition.options[0].id, name: "Beech" }, signal);
    expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/store/attributes/${definition.id}/options/${definition.options[0].id}`);
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: "Beech" });
    expect(fetch.mock.calls[0][1].signal).toBe(signal);
  });
  it("sends the full merchant order with no locally generated option codes", async () => {
    const options = [definition.options[2], definition.options[0], definition.options[1]], optionIds = options.map(option => option.id);
    const fetch = serve({ ...definition, options });
    await changeAttribute("store", definition, { kind: "reorder", optionIds }, new AbortController().signal);
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ optionIds });
    expect(fetch.mock.calls[0][1].method).toBe("PUT");
  });
  it.each([definition, { ...definition, options: [definition.options[2]] }])("requires the exact full returned order %j", async value => {
    serve(value); await expect(api.reorderOptions("store", definition.id, [definition.options[2].id, definition.options[0].id, definition.options[1].id])).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each([
    { ...definition, code: "changed" }, { ...definition, isInFeeds: false },
    { ...definition, options: definition.options.map((o, i) => i === 0 ? { ...o, name: "Solid oak", code: "solid-oak" } : o) },
  ])("requires option writes to preserve definition identity, flags and stable option codes %j", async value => {
    serve(value); await expect(changeAttribute("store", definition, { kind: "rename", optionId: definition.options[0].id, name: "Solid oak" }, new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("uses existing bodyless deletes, accepts only empty answers and retains in-use details", async () => {
    const fetch = serve(undefined, 204); const signal = new AbortController().signal;
    await api.removeOption("store/a", definition.id, "option/1", signal); await api.removeAttribute("store/a", definition.id, signal);
    expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body])).toEqual([
      [`/api/admin/stores/store%2Fa/attributes/${definition.id}/options/option%2F1`, "DELETE", undefined],
      [`/api/admin/stores/store%2Fa/attributes/${definition.id}`, "DELETE", undefined],
    ]);
    serve({ done: true }); await expect(api.removeAttribute("store", definition.id)).rejects.toMatchObject({ kind: "invalid-response" });
    const problem = { title: "This attribute cannot be removed", detail: "2 categories offer it as a filter. Clear them first." };
    serve(problem, 409); await expect(api.removeAttribute("store", definition.id)).rejects.toMatchObject({ status: 409, problem });
  });
  it("does not infer an empty collection from a failed read", async () => {
    serve({ title: "Unavailable" }, 503); await expect(api.attributes("store")).rejects.toMatchObject({ status: 503 });
  });
  it("strips undeclared definition and option facts before future writes or stale comparisons", async () => {
    serve([{ ...definition, dangerous: "ignored", options: [{ ...definition.options[0], url: "https://elsewhere.invalid" }] }]);
    const [item] = await api.attributes("store"); expect(item).not.toHaveProperty("dangerous"); expect(item.options[0]).not.toHaveProperty("url");
  });
});
