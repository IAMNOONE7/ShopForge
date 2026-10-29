import type { AdminStore, FreeShippingSettings, StoreSettings } from "../api";
import { bulkPrice } from "../utils/bulkListings";
import { HttpError } from "./http";

export function validFreeShipping(value: unknown, currency: string): value is FreeShippingSettings {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const { isOn, threshold } = value as Record<string, unknown>;
  return typeof isOn === "boolean" && (threshold === null ? !isOn :
    typeof threshold === "number" && threshold > 0 && bulkPrice(String(threshold), currency) === threshold);
}

export function storeSettingsVersion(store: AdminStore) {
  return JSON.stringify([store.id, store.status, store.name, store.currency, store.culture, store.returnWindowDays,
    store.theme.primaryColor, store.theme.secondaryColor, store.theme.borderRadius,
    store.company && [store.company.legalName, store.company.line1, store.company.city, store.company.postalCode,
      store.company.country, store.company.registrationNumber, store.company.vatNumber],
    store.freeShipping && [store.freeShipping.isOn, store.freeShipping.threshold]]);
}

export function checkedSettingsSave(value: AdminStore, previous: AdminStore, input: StoreSettings): AdminStore {
  const expected = { ...previous, ...input, culture: Intl.getCanonicalLocales(input.culture)[0], company: input.company ?? previous.company,
    freeShipping: input.freeShipping ?? value?.freeShipping };
  if (!value || value.id !== previous.id || !value.theme || value.company === undefined ||
    value.freeShipping !== undefined && !validFreeShipping(value.freeShipping, input.currency) ||
    previous.freeShipping !== undefined && value.freeShipping === undefined ||
    storeSettingsVersion(value) !== storeSettingsVersion(expected)) throw new HttpError("invalid-response", 200);
  return value;
}
