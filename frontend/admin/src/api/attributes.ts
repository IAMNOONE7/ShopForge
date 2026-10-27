import type { AttributeDefinition, AttributeInput, AttributeUpdate } from "../api";
import { HttpError, requestJson } from "./http";

const types = ["text", "integer", "decimal", "boolean", "date", "select", "multiSelect"];
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const code = (value: unknown): value is string => typeof value === "string" && value.length <= 120 && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value);

function definition(value: unknown): value is AttributeDefinition {
  return object(value) && text(value.id) && code(value.code) && text(value.name) && types.includes(value.type as string) &&
    (value.unit === null || typeof value.unit === "string") &&
    ["isFilterable", "isVisibleOnProductPage", "isSearchable", "isInFeeds"].every(key => typeof value[key] === "boolean") &&
    !(value.type === "text" && value.isFilterable) && !(value.type !== "text" && value.isSearchable) &&
    Number.isInteger(value.sortOrder) && Number(value.sortOrder) >= -2147483648 && Number(value.sortOrder) <= 2147483647 &&
    Array.isArray(value.options) && value.options.every(option => object(option) && text(option.id) && code(option.code) && text(option.name)) &&
    new Set(value.options.map(option => option.id)).size === value.options.length &&
    new Set(value.options.map(option => option.code)).size === value.options.length &&
    (["select", "multiSelect"].includes(value.type as string) || value.options.length === 0);
}

function checked(value: unknown, id?: string): AttributeDefinition {
  if (!definition(value) || id !== undefined && value.id !== id) throw new HttpError("invalid-response", 200);
  return { id: value.id, code: value.code, name: value.name, type: value.type, unit: value.unit,
    isFilterable: value.isFilterable, isVisibleOnProductPage: value.isVisibleOnProductPage, isInFeeds: value.isInFeeds,
    isSearchable: value.isSearchable, sortOrder: value.sortOrder, options: value.options.map(option => ({ id: option.id, code: option.code, name: option.name })) };
}
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/attributes`;
const path = (storeId: string, id: string) => `${root(storeId)}/${encodeURIComponent(id)}`;

export async function getAttributes(storeId: string, signal?: AbortSignal) {
  const value = await requestJson<unknown>(root(storeId), { signal });
  if (!Array.isArray(value) || !value.every(definition) || new Set(value.map(item => item.id)).size !== value.length ||
    new Set(value.map(item => item.code)).size !== value.length) throw new HttpError("invalid-response", 200);
  return value.map(item => checked(item));
}
export async function createAttribute(storeId: string, input: AttributeInput, signal?: AbortSignal) {
  const value = checked(await requestJson<unknown>(root(storeId), { method: "POST", body: input, signal }));
  if (value.type !== input.type || input.code !== null && value.code !== input.code || !matchesSettings(value, input) ||
    JSON.stringify(value.options.map(option => option.name)) !== JSON.stringify(input.options)) throw new HttpError("invalid-response", 200);
  return value;
}
function matchesSettings(value: AttributeDefinition, input: AttributeUpdate | AttributeInput) {
  return value.name === input.name && value.unit === input.unit && value.sortOrder === input.sortOrder &&
    value.isFilterable === input.isFilterable && value.isVisibleOnProductPage === input.isVisibleOnProductPage &&
    value.isInFeeds === (input.isInFeeds ?? false) && value.isSearchable === (input.isSearchable ?? false);
}
export async function updateAttribute(storeId: string, id: string, input: AttributeUpdate, signal?: AbortSignal) {
  const value = checked(await requestJson<unknown>(path(storeId, id), { method: "PUT", body: input, signal }), id);
  if (!matchesSettings(value, input)) throw new HttpError("invalid-response", 200);
  return value;
}
export async function addAttributeOption(storeId: string, id: string, name: string, signal?: AbortSignal) {
  const value = checked(await requestJson<unknown>(path(storeId, id) + "/options", { method: "POST", body: { name }, signal }), id);
  if (!value.options.some(option => option.name === name)) throw new HttpError("invalid-response", 200);
  return value;
}
export async function renameAttributeOption(storeId: string, id: string, optionId: string, name: string, signal?: AbortSignal) {
  const value = checked(await requestJson<unknown>(`${path(storeId, id)}/options/${encodeURIComponent(optionId)}`, { method: "PUT", body: { name }, signal }), id);
  if (value.options.find(option => option.id === optionId)?.name !== name) throw new HttpError("invalid-response", 200);
  return value;
}
export async function reorderAttributeOptions(storeId: string, id: string, optionIds: string[], signal?: AbortSignal) {
  if (new Set(optionIds).size !== optionIds.length) throw new RangeError("Duplicate option identity.");
  const value = checked(await requestJson<unknown>(path(storeId, id) + "/options", { method: "PUT", body: { optionIds }, signal }), id);
  if (JSON.stringify(value.options.map(option => option.id)) !== JSON.stringify(optionIds)) throw new HttpError("invalid-response", 200);
  return value;
}
export async function removeAttribute(storeId: string, id: string, signal?: AbortSignal) {
  const value = await requestJson<unknown>(path(storeId, id), { method: "DELETE", signal });
  if (value !== undefined) throw new HttpError("invalid-response", 200);
}
export async function removeAttributeOption(storeId: string, id: string, optionId: string, signal?: AbortSignal) {
  const value = await requestJson<unknown>(`${path(storeId, id)}/options/${encodeURIComponent(optionId)}`, { method: "DELETE", signal });
  if (value !== undefined) throw new HttpError("invalid-response", 200);
}
