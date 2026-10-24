import { HttpError, requestJson } from "./http";

export type SetupSummary = {
  id: string; kind: string; subject: string | null; state: string; currentStep: string | null; createdAt: string; finishedAt: string | null;
};
export type SetupStep = {
  name: string; status: string; resourceId: string | null; created: boolean; requestId: string | null; keepsSecret: boolean;
  attempts: number; checks: number; waitingSince: string | null; note: string | null; lastError: string | null;
  lastErrorAt: string | null; startedAt: string | null; finishedAt: string | null;
};
export type SetupDetail = Omit<SetupSummary, "currentStep"> & {
  changedAt: string; nextCheckAt: string | null; stepRunning: boolean; actions: string[]; steps: SetupStep[];
};
export const setupActions = ["retry", "continue", "cancel"] as const;
export type SetupAction = typeof setupActions[number];
export const isSetupId = (value: string) => /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
const invalid = () => new HttpError("invalid-response", 200);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const nullableText = (value: unknown): value is string | null => value === null || typeof value === "string";
const date = (value: unknown): value is string => typeof value === "string" && /T.*(?:Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value));
const nullableDate = (value: unknown): value is string | null => value === null || date(value);
function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw invalid();
  return value as Record<string, unknown>;
}
function base(value: unknown) {
  const row = record(value);
  if (typeof row.id !== "string" || !isSetupId(row.id) || !text(row.kind) || !text(row.state) || !nullableText(row.subject)
    || !date(row.createdAt) || !nullableDate(row.finishedAt)) throw invalid();
  return { id: row.id, kind: row.kind, subject: row.subject, state: row.state, createdAt: row.createdAt, finishedAt: row.finishedAt };
}
function step(value: unknown): SetupStep {
  const row = record(value);
  if (!text(row.name) || !text(row.status) || !nullableText(row.resourceId) || !nullableText(row.requestId) || !nullableText(row.note) || !nullableText(row.lastError)
    || typeof row.created !== "boolean" || typeof row.keepsSecret !== "boolean"
    || typeof row.attempts !== "number" || !Number.isSafeInteger(row.attempts) || row.attempts < 0
    || typeof row.checks !== "number" || !Number.isSafeInteger(row.checks) || row.checks < 0
    || !nullableDate(row.waitingSince) || !nullableDate(row.lastErrorAt) || !nullableDate(row.startedAt) || !nullableDate(row.finishedAt)) throw invalid();
  return { name: row.name, status: row.status, resourceId: row.resourceId, created: row.created, requestId: row.requestId, keepsSecret: row.keepsSecret,
    attempts: row.attempts, checks: row.checks, waitingSince: row.waitingSince, note: row.note, lastError: row.lastError,
    lastErrorAt: row.lastErrorAt, startedAt: row.startedAt, finishedAt: row.finishedAt };
}
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/operations`;
function checkedDetail(value: unknown, operationId: string): SetupDetail {
  const row = record(value), item = base(row);
  if (item.id.toLowerCase() !== operationId.toLowerCase() || typeof row.stepRunning !== "boolean" || !date(row.changedAt) || !nullableDate(row.nextCheckAt)
    || !Array.isArray(row.actions) || !row.actions.every(text) || !Array.isArray(row.steps)) throw invalid();
  return { ...item, stepRunning: row.stepRunning, changedAt: row.changedAt, nextCheckAt: row.nextCheckAt, actions: [...row.actions], steps: row.steps.map(step) };
}
export const setupProgressApi = {
  list: async (storeId: string, signal?: AbortSignal): Promise<SetupSummary[]> => {
    const value = await requestJson<unknown>(root(storeId), { signal }); signal?.throwIfAborted();
    if (!Array.isArray(value) || value.length > 50) throw invalid();
    const items = value.map(value => {
      const row = record(value), item = base(row);
      if (!nullableText(row.currentStep)) throw invalid();
      return { ...item, currentStep: row.currentStep };
    });
    if (new Set(items.map(item => item.id.toLowerCase())).size !== items.length) throw invalid();
    return items;
  },
  detail: async (storeId: string, operationId: string, signal?: AbortSignal): Promise<SetupDetail> => {
    const value = await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(operationId)}`, { signal }); signal?.throwIfAborted();
    return checkedDetail(value, operationId);
  },
  act: async (storeId: string, operationId: string, action: SetupAction, signal?: AbortSignal): Promise<SetupDetail> => {
    if (!isSetupId(operationId) || !setupActions.some(value => value === action)) throw invalid();
    const value = await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(operationId)}/${action}`, { method: "POST", signal }); signal?.throwIfAborted();
    return checkedDetail(value, operationId);
  },
};
export const setupIsActive = (state: string) => state === "running" || state === "cancelling";
export function setupRecoverySupported(item: SetupDetail) {
  return ["email.domain", "email.domain.remove", "domain.bind", "domain.unbind", "domain.revalidate", "domain.zone"].includes(item.kind)
    && ["running", "awaitingInput", "failed", "succeeded", "cancelling", "cancelled"].includes(item.state)
    && item.actions.every(action => setupActions.some(known => known === action))
    && item.steps.every(step => ["pending", "waiting", "awaitingInput", "succeeded", "failed", "undone", "kept"].includes(step.status));
}
export function offeredSetupActions(item: SetupDetail): SetupAction[] {
  if (!setupRecoverySupported(item)) return [];
  return setupActions.filter(action => item.actions.includes(action) && (
    action === "retry" ? item.state === "failed" : action === "continue" ? item.state === "awaitingInput" : ["running", "awaitingInput", "failed"].includes(item.state)));
}
