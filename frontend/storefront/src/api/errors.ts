import { isHttpError, type HttpError } from "./http";

export type ErrorOperation =
  | "read"
  | "write"
  | "login"
  | "session"
  | "checkout"
  | "download";
export type ErrorMessageKey =
  | "network"
  | "invalidResponse"
  | "validation"
  | "credentials"
  | "sessionExpired"
  | "permissionDenied"
  | "notFound"
  | "conflict"
  | "tooLarge"
  | "rateLimited"
  | "unavailable"
  | "download"
  | "request"
  | "action";

export type FieldIssue = {
  field: string;
  target: string;
  key: ValidationMessageKey;
};
export type ValidationMessageKey =
  | "emailInvalid"
  | "passwordInvalid"
  | "nameInvalid"
  | "quantityInvalid"
  | "addressInvalid"
  | "methodInvalid"
  | "countryInvalid"
  | "fileInvalid"
  | "codeInvalid"
  | "numberInvalid"
  | "selectionInvalid"
  | "valueInvalid"
  | "filterInvalid"
  | "fieldInvalid";

export function errorMessageKey(
  error: unknown,
  operation: ErrorOperation,
): ErrorMessageKey {
  if (!isHttpError(error)) return operation === "read" ? "request" : "action";
  if (error.kind === "network") return "network";
  if (error.kind === "invalid-response") return "invalidResponse";
  if (error.problem?.errors && Object.keys(error.problem.errors).length > 0)
    return "validation";
  if (operation === "login" && error.status === 401) return "credentials";
  if (operation === "session" && error.status === 401) return "sessionExpired";
  if (error.status === 403) return "permissionDenied";
  if (error.status === 404) return "notFound";
  if (error.status === 409) return "conflict";
  if (error.status === 413) return "tooLarge";
  if (error.status === 429) return "rateLimited";
  if (error.status !== null && error.status >= 500) return "unavailable";
  if (operation === "download") return "download";
  return operation === "read" ? "request" : "action";
}

export function fieldIssues(error: unknown): FieldIssue[] {
  if (!isHttpError(error)) return [];
  return Object.keys(error.problem?.errors ?? {}).map((field) => ({
    field,
    target: targetName(field),
    key: validationKey(field),
  }));
}

export function traceId(error: unknown) {
  return isHttpError(error) ? error.traceId : null;
}

export function statusOf(error: unknown) {
  return isHttpError(error) ? error.status : null;
}

function targetName(field: string) {
  const normalized = field
    .replace(/^\$\.?/, "")
    .replace(/\[(\w+)\]/g, ".$1")
    .split(".")
    .map((part) => (part ? part[0].toLowerCase() + part.slice(1) : part))
    .join(".");
  if (normalized === "billingAddress") return "billing.fullName";
  if (normalized === "shippingAddress") return "shipping.fullName";
  if (normalized.startsWith("values."))
    return `attribute:${normalized.slice("values.".length)}`;
  return normalized;
}

function validationKey(field: string): ValidationMessageKey {
  const normalized = field.toLowerCase();
  if (normalized === "email") return "emailInvalid";
  if (normalized === "password") return "passwordInvalid";
  if (
    normalized === "firstname" ||
    normalized === "lastname" ||
    normalized === "name"
  )
    return "nameInvalid";
  if (normalized === "quantity") return "quantityInvalid";
  if (normalized.includes("address")) return "addressInvalid";
  if (
    normalized === "paymentmethodcode" ||
    normalized === "shippingmethodcode" ||
    normalized === "pickuppointcode"
  )
    return "methodInvalid";
  if (normalized === "country") return "countryInvalid";
  if (normalized === "file") return "fileInvalid";
  if (
    normalized === "code" ||
    normalized === "sku" ||
    normalized === "ean" ||
    normalized === "slug"
  )
    return "codeInvalid";
  if (
    [
      "price",
      "vatrate",
      "weightgrams",
      "returnwindowdays",
      "value",
      "minimumorderamount",
      "maxredemptions",
      "maxredemptionspercustomer",
    ].includes(normalized)
  )
    return "numberInvalid";
  if (
    normalized === "categoryids" ||
    normalized === "attributeids" ||
    normalized === "productid" ||
    normalized === "providerkey" ||
    normalized === "type" ||
    normalized === "options"
  )
    return "selectionInvalid";
  if (normalized.startsWith("values.")) return "valueInvalid";
  if (normalized.startsWith("f.") || normalized === "sort")
    return "filterInvalid";
  return "fieldInvalid";
}

export function httpError(error: unknown): HttpError | null {
  return isHttpError(error) ? error : null;
}
