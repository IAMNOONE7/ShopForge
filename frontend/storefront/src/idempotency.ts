import { isHttpError } from "./api/http";

export type WriteAttempt<T> = {
  key: string;
  payload: T;
  body: string;
};

// Both endpoints fingerprint the raw JSON body, so a replay must keep its exact payload and field order.
export function attemptFor<T>(previous: WriteAttempt<T> | null, payload: T): WriteAttempt<T> {
  const body = JSON.stringify(payload);
  if (previous?.body === body) return previous;
  const bytes = globalThis.crypto.getRandomValues(new Uint8Array(16));
  const key = "sf-" + Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
  return { key, payload, body };
}

export function changedSince<T>(attempt: WriteAttempt<T> | null, payload: T) {
  return attempt !== null && attempt.body !== JSON.stringify(payload);
}

export function uncertainWrite(error: unknown) {
  if (!isHttpError(error)) return true;
  return error.kind !== "http" || error.status === null || error.status >= 500;
}

export function inFlightWrite(error: unknown) {
  return isHttpError(error) && error.status === 409 &&
    error.problem?.title === "A request with this key is still in progress";
}
