import { isHttpError } from "../api/http";

export function listingVisibilityMessages(error: unknown, field: "isVisible" | "storeProductIds"): string[] {
  if (!isHttpError(error) || error.kind !== "http" || error.status !== 400) return [];
  return [...new Set(Object.entries(error.problem?.errors ?? {})
    .filter(([key]) => key.toLowerCase() === field.toLowerCase())
    .flatMap(([, messages]) => Array.isArray(messages) ? messages.filter((message): message is string => typeof message === "string" && message.trim().length > 0) : []))];
}
