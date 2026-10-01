import type { AttributeType } from "../api";

export const attributeTypes: AttributeType[] = [
  "select", "multiSelect", "decimal", "integer", "boolean", "date", "text",
];

export type AttributeDraft = {
  name: string;
  code: string;
  type: AttributeType;
  unit: string;
  isFilterable: boolean;
  isVisibleOnProductPage: boolean;
  sortOrder: string;
  options: string;
};

export type AttributeField = "name" | "code" | "unit" | "sortOrder" | "options";
export type AttributeIssueKey =
  | "nameInvalid" | "codeInvalid" | "unitInvalid" | "sortOrderInvalid"
  | "optionsInvalid" | "optionsDuplicate";
export type AttributeIssue = { field: AttributeField; key: AttributeIssueKey };

const codePattern = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

export function generatedCode(value: string) {
  let code = "";
  for (const character of value.normalize("NFD").toLowerCase()) {
    if (/^[a-z0-9]$/.test(character)) code += character;
    else if (!/^\p{Mn}$/u.test(character) && code && !code.endsWith("-")) code += "-";
  }
  return code.replace(/-+$/, "").slice(0, 120).replace(/-+$/, "");
}

export function validCode(code: string) {
  return code.length > 0 && code.length <= 120 && codePattern.test(code);
}

export function optionNames(value: string) {
  return value.split(/\r?\n/).map((name) => name.trim()).filter(Boolean);
}

export function validateAttribute(draft: AttributeDraft, creating: boolean): AttributeIssue[] {
  const issues: AttributeIssue[] = [];
  const name = draft.name.trim();
  if (!name || name.length > 200) issues.push({ field: "name", key: "nameInvalid" });
  if (creating && !validCode(draft.code.trim() || generatedCode(name))) {
    issues.push({ field: "code", key: "codeInvalid" });
  }
  if (draft.unit.trim().length > 20) issues.push({ field: "unit", key: "unitInvalid" });
  if (!/^-?[0-9]+$/.test(draft.sortOrder.trim())
    || Number(draft.sortOrder) < -2147483648
    || Number(draft.sortOrder) > 2147483647) {
    issues.push({ field: "sortOrder", key: "sortOrderInvalid" });
  }
  if (creating && draft.options.trim()) {
    const names = optionNames(draft.options);
    if (!["select", "multiSelect"].includes(draft.type)
      || names.some((option) => option.length > 200 || !generatedCode(option))) {
      issues.push({ field: "options", key: "optionsInvalid" });
    } else if (new Set(names.map(generatedCode)).size !== names.length) {
      issues.push({ field: "options", key: "optionsDuplicate" });
    }
  }
  return issues;
}

export function validateOption(name: string, existingCodes: string[]): AttributeIssueKey | null {
  const trimmed = name.trim();
  if (!trimmed || trimmed.length > 200 || !generatedCode(trimmed)) return "optionsInvalid";
  if (existingCodes.includes(generatedCode(trimmed))) return "optionsDuplicate";
  return null;
}
