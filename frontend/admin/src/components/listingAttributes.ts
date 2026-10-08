import type { AttributeDefinition, AttributeValues } from "../api";
import { decimalNumber } from "./listingValidation";

export type AttributeDraft = Record<string, string | string[]>;
export function attributeDraft(attributes: AttributeDefinition[], values: AttributeValues): AttributeDraft {
  return Object.fromEntries(attributes.map((attribute) => {
    const value = values[attribute.code];
    return [attribute.code, attribute.type === "multiSelect" ? (Array.isArray(value) ? [...value] : []) : value === undefined ? "" : String(value)];
  }));
}
export function validDate(text: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(text) || text.startsWith("0000")) return false;
  const date = new Date(`${text}T00:00:00Z`);
  return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0, 10) === text;
}
export function validDecimal(text: string) {
  const normalized = text.trim().replace(",", ".");
  if (!/^-?\d+(?:\.\d{1,28})?$/.test(normalized)) return false;
  const significant = normalized.includes(".") ? normalized.replace(/0+$/, "") : normalized;
  const digits = significant.replace(/[.-]/g, "").replace(/^0+/, "");
  return digits.length <= 15 && Number.isFinite(decimalNumber(text));
}
export function validAttribute(attribute: AttributeDefinition, value: string | string[]) {
  if (Array.isArray(value)) return attribute.type === "multiSelect" && new Set(value).size === value.length && value.every((code) => attribute.options.some((option) => option.code === code));
  if (value === "") return true;
  switch (attribute.type) {
    case "text": return !!value.trim() && value.trim().length <= 500;
    case "integer": return /^-?\d+$/.test(value.trim()) && Number.isSafeInteger(Number(value));
    case "decimal": return validDecimal(value);
    case "boolean": return value === "true" || value === "false";
    case "date": return validDate(value);
    case "select": return attribute.options.some((option) => option.code === value);
    default: return false;
  }
}
export function attributeInput(attributes: AttributeDefinition[], draft: AttributeDraft): AttributeValues {
  const values: AttributeValues = {};
  for (const attribute of attributes) {
    const raw = draft[attribute.code];
    if (raw === "" || raw === undefined || (Array.isArray(raw) && raw.length === 0)) continue;
    if (Array.isArray(raw)) values[attribute.code] = raw;
    else values[attribute.code] = ["integer", "decimal"].includes(attribute.type) ? decimalNumber(raw) : attribute.type === "boolean" ? raw === "true" : raw.trim();
  }
  return values;
}

export function attributeLabel(attribute: AttributeDefinition, value: AttributeValues[string] | undefined, yes: string, no: string, locale: string): string {
  if (value === undefined) return "—";
  if (attribute.type === "boolean") return value === true ? yes : no;
  if (["select", "multiSelect"].includes(attribute.type)) return (Array.isArray(value) ? value : [String(value)]).map((code) => attribute.options.find((option) => option.code === code)?.name ?? code).join(", ");
  if (typeof value === "number") return new Intl.NumberFormat(locale, { maximumSignificantDigits: 21 }).format(value);
  if (attribute.type === "date" && typeof value === "string" && validDate(value)) return new Intl.DateTimeFormat(locale, { timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
  return String(value);
}
