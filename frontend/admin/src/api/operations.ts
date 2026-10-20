import type { FailedMessage } from "../api";
import { HttpError, requestJson } from "./http";

export type SuppressedAddress = {
  id: string;
  email: string;
  reason: string;
  detail: string | null;
  suppressedAt: string;
};

const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}`;
const invalid = () => new HttpError("invalid-response", 200);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const nullableText = (value: unknown) => value === null || typeof value === "string";
const date = (value: unknown): value is string => text(value) && /^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value));

function rows(value: unknown, limit: number): Record<string, unknown>[] {
  if (!Array.isArray(value) || value.length > limit) throw invalid();
  const ids = new Set<string>();
  for (const row of value) {
    if (typeof row !== "object" || row === null || typeof row.id !== "string"
      || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(row.id)
      || ids.has(row.id.toLowerCase())) throw invalid();
    ids.add(row.id.toLowerCase());
  }
  return value;
}

export async function getFailedMessages(storeId: string, signal?: AbortSignal): Promise<FailedMessage[]> {
  const value = await requestJson<unknown>(`${root(storeId)}/failed-messages`, { signal });
  signal?.throwIfAborted();
  return rows(value, 100).map(row => {
    if (!text(row.type) || !Number.isSafeInteger(row.attempts) || (row.attempts as number) < 0
      || !date(row.createdAt) || !nullableText(row.error)) throw invalid();
    return row as FailedMessage;
  });
}

export async function getSuppressedAddresses(storeId: string, signal?: AbortSignal): Promise<SuppressedAddress[]> {
  const value = await requestJson<unknown>(`${root(storeId)}/suppressed-addresses`, { signal });
  signal?.throwIfAborted();
  return rows(value, 200).map(row => {
    if (!text(row.email) || !text(row.reason) || !nullableText(row.detail) || !date(row.suppressedAt)) throw invalid();
    return row as SuppressedAddress;
  });
}

async function emptyChange(url: string, method: "POST" | "DELETE") {
  const value = await requestJson<unknown>(url, { method });
  if (value !== undefined) throw invalid();
}

export function requeueMessage(storeId: string, messageId: string) {
  return emptyChange(`${root(storeId)}/failed-messages/${encodeURIComponent(messageId)}/requeue`, "POST");
}

export function allowSuppressedAddress(storeId: string, id: string) {
  return emptyChange(`${root(storeId)}/suppressed-addresses/${encodeURIComponent(id)}`, "DELETE");
}
