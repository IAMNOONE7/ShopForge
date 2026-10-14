import type { Company } from "../api";
import { isHttpError } from "../api/http";

export type ThemeDraft = {
  primaryColor: string;
  secondaryColor: string;
  borderRadius: string;
};

export type CreateStoreDraft = ThemeDraft & {
  name: string;
  hostName: string;
  currency: string;
  culture: string;
};

export type StoreSettingsDraft = ThemeDraft & {
  name: string;
  currency: string;
  culture: string;
  returnWindowDays: string;
  legalName: string;
  line1: string;
  city: string;
  postalCode: string;
  country: string;
  registrationNumber: string;
  vatNumber: string;
};

export type StoreIssueKey =
  | "nameInvalid"
  | "hostInvalid"
  | "hostRejected"
  | "hostTaken"
  | "hostUnavailable"
  | "currencyInvalid"
  | "cultureInvalid"
  | "colorInvalid"
  | "radiusInvalid"
  | "returnWindowInvalid"
  | "companyIncomplete"
  | "countryInvalid"
  | "fileTypeInvalid"
  | "fileSizeInvalid"
  | "fileEmpty";

export type StoreField = keyof StoreSettingsDraft | keyof CreateStoreDraft | "logo";

export type StoreIssue = {
  field: StoreField;
  key: StoreIssueKey;
};

const hexColor = /^#[0-9a-f]{6}$/i;
const currency = /^[a-z]{3}$/i;
const country = /^[a-z]{2}$/i;

export function validateCreateStore(draft: CreateStoreDraft): StoreIssue[] {
  return [
    ...validateName(draft.name),
    ...(isStoreAddress(draft.hostName)
      ? []
      : [{ field: "hostName", key: "hostInvalid" } satisfies StoreIssue]),
    ...validateFormatting(draft),
    ...validateTheme(draft),
  ];
}

export function validateStoreSettings(
  draft: StoreSettingsDraft,
): StoreIssue[] {
  const issues = [
    ...validateName(draft.name),
    ...validateFormatting(draft),
    ...validateTheme(draft),
  ];
  const returnWindow = Number(draft.returnWindowDays);
  if (
    !/^\d+$/.test(draft.returnWindowDays.trim()) ||
    !Number.isInteger(returnWindow) ||
    returnWindow < 0 ||
    returnWindow > 365
  ) {
    issues.push({
      field: "returnWindowDays",
      key: "returnWindowInvalid",
    });
  }

  const companyFields = [
    "legalName",
    "line1",
    "city",
    "postalCode",
    "country",
    "registrationNumber",
    "vatNumber",
  ] as const;
  const hasCompany = companyFields.some((field) => draft[field].trim() !== "");
  if (hasCompany) {
    for (const field of companyFields.slice(0, 6)) {
      if (!draft[field].trim()) {
        issues.push({ field, key: "companyIncomplete" });
      }
    }
    if (draft.country.trim() && !country.test(draft.country.trim())) {
      issues.push({ field: "country", key: "countryInvalid" });
    }
  }

  return issues;
}

export function companyFromDraft(
  draft: StoreSettingsDraft,
): Company | undefined {
  const values = [
    draft.legalName,
    draft.line1,
    draft.city,
    draft.postalCode,
    draft.country,
    draft.registrationNumber,
    draft.vatNumber,
  ];
  if (values.every((value) => !value.trim())) return undefined;
  return {
    legalName: draft.legalName.trim(),
    line1: draft.line1.trim(),
    city: draft.city.trim(),
    postalCode: draft.postalCode.trim(),
    country: draft.country.trim().toUpperCase(),
    registrationNumber: draft.registrationNumber.trim(),
    vatNumber: draft.vatNumber.trim() || null,
  };
}

export function validateLogo(file: File): StoreIssue | null {
  if (file.size === 0) return { field: "logo", key: "fileEmpty" };
  if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) {
    return { field: "logo", key: "fileTypeInvalid" };
  }
  if (file.size > 5 * 1024 * 1024) {
    return { field: "logo", key: "fileSizeInvalid" };
  }
  return null;
}

export function normalizedTheme(draft: ThemeDraft) {
  return {
    primaryColor: draft.primaryColor.trim().toUpperCase(),
    secondaryColor: draft.secondaryColor.trim().toUpperCase(),
    borderRadius: Number(draft.borderRadius),
  };
}

function validateName(name: string): StoreIssue[] {
  return name.trim() && name.trim().length <= 200
    ? []
    : [{ field: "name", key: "nameInvalid" }];
}

function validateFormatting(draft: {
  currency: string;
  culture: string;
}): StoreIssue[] {
  const issues: StoreIssue[] = [];
  if (!currency.test(draft.currency.trim())) {
    issues.push({ field: "currency", key: "currencyInvalid" });
  }
  try {
    if (!draft.culture.trim()) throw new RangeError();
    const canonical = Intl.getCanonicalLocales(draft.culture.trim())[0];
    const resolved = new Intl.NumberFormat(canonical).resolvedOptions().locale;
    if (resolved.toLowerCase() !== canonical.toLowerCase()) throw new RangeError();
  } catch {
    issues.push({ field: "culture", key: "cultureInvalid" });
  }
  return issues;
}

function validateTheme(draft: ThemeDraft): StoreIssue[] {
  const issues: StoreIssue[] = [];
  if (!hexColor.test(draft.primaryColor.trim())) {
    issues.push({ field: "primaryColor", key: "colorInvalid" });
  }
  if (!hexColor.test(draft.secondaryColor.trim())) {
    issues.push({ field: "secondaryColor", key: "colorInvalid" });
  }
  const radius = Number(draft.borderRadius);
  if (
    !/^\d+$/.test(draft.borderRadius.trim()) ||
    !Number.isInteger(radius) ||
    radius < 0
  ) {
    issues.push({ field: "borderRadius", key: "radiusInvalid" });
  }
  return issues;
}

export function normalizedStoreAddress(value: string) {
  return value.trim().replace(/\.+$/, "").toLowerCase();
}

function isStoreAddress(value: string) {
  const input = normalizedStoreAddress(value);
  const [label, ...suffix] = input.split(".");
  // The server owns the suffix. Only address syntax can be checked before submitting.
  return input.length <= 253 && label.length > 0 && label.length <= 63
    && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(label)
    && suffix.every((part) => /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(part));
}

export function storeAddressIssue(error: unknown): StoreIssue | null {
  if (!isHttpError(error)) return null;
  // These responses have no reason codes. Unknown responses retain the shared error fallback.
  if (error.status === 409 && error.problem?.title === "The host name is already used by another store") {
    return { field: "hostName", key: "hostTaken" };
  }
  const messages = error.problem?.errors?.hostName;
  if (error.status !== 400 || !messages?.length) return null;
  return {
    field: "hostName",
    key: messages.includes("The platform has no host name suffix configured, so it cannot give a store an address.")
      ? "hostUnavailable" : "hostRejected",
  };
}

export function issueText(
  issues: StoreIssue[],
  field: StoreField,
  translate: (key: `validation.${StoreIssueKey}`) => string,
) {
  const issue = issues.find((candidate) => candidate.field === field);
  return issue ? translate(`validation.${issue.key}`) : undefined;
}
