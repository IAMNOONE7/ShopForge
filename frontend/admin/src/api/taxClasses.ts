import { HttpError, requestJson } from "./http";

export type TaxRate = { country: string; rate: number };
export type TaxClass = { code: string; name: string; rates: TaxRate[] };

export const validTaxCode = (value: string) => value.length <= 50 && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value);
export const validTaxName = (value: string) => value.trim().length > 0 && value.trim().length <= 100;
export const taxCountry = (value: string) => /^[a-z]{2}$/i.test(value.trim()) ? value.trim().toUpperCase() : null;
export const validTaxRate = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value)
  && value >= 0 && value <= 100 && Math.round(value * 100) / 100 === value;

export function parseTaxPercentage(value: string): number | null {
  const text = value.trim();
  if (!/^\d{1,3}(?:[.,]\d{1,2})?$/.test(text)) return null;
  const number = Number(text.replace(",", "."));
  return validTaxRate(number) ? number : null;
}
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/tax-classes`;
const itemPath = (storeId: string, code: string) => `${root(storeId)}/${encodeURIComponent(code)}`;
const invalid = (status = 200) => new HttpError("invalid-response", status);

function checkedRate(value: unknown, status = 200): TaxRate {
  if (typeof value !== "object" || value === null) throw invalid(status);
  const row = value as TaxRate;
  if (typeof row.country !== "string" || taxCountry(row.country) !== row.country || !validTaxRate(row.rate)) throw invalid(status);
  return { country: row.country, rate: row.rate };
}

function checkedClass(value: unknown, status = 200): TaxClass {
  if (typeof value !== "object" || value === null) throw invalid(status);
  const row = value as TaxClass;
  if (typeof row.code !== "string" || !validTaxCode(row.code) || typeof row.name !== "string" || !validTaxName(row.name)
    || row.name.trim() !== row.name || !Array.isArray(row.rates)) throw invalid(status);
  const rates = row.rates.map(rate => checkedRate(rate, status));
  if (new Set(rates.map(rate => rate.country)).size !== rates.length) throw invalid(status);
  return { code: row.code, name: row.name, rates: rates.sort((a, b) => a.country.localeCompare(b.country)) };
}

export async function getTaxClasses(storeId: string, signal?: AbortSignal): Promise<TaxClass[]> {
  const value = await requestJson<unknown>(root(storeId), { signal });
  signal?.throwIfAborted();
  if (!Array.isArray(value)) throw invalid();
  const classes = value.map(row => checkedClass(row));
  if (new Set(classes.map(row => row.code)).size !== classes.length) throw invalid();
  return classes;
}

export async function createTaxClass(storeId: string, code: string, name: string) {
  if (!validTaxCode(code) || !validTaxName(name)) throw new RangeError("Invalid tax class.");
  const saved = checkedClass(await requestJson<unknown>(root(storeId), { method: "POST", body: { code, name: name.trim() } }), 201);
  if (saved.code !== code || saved.name !== name.trim() || saved.rates.length !== 0) throw invalid(201);
  return saved;
}

export async function renameTaxClass(storeId: string, code: string, name: string) {
  if (!validTaxCode(code) || !validTaxName(name)) throw new RangeError("Invalid tax class.");
  const saved = checkedClass(await requestJson<unknown>(itemPath(storeId, code), { method: "PUT", body: { name: name.trim() } }));
  if (saved.code !== code || saved.name !== name.trim()) throw invalid();
  // The rename reply has no saved rates; callers refresh the collection instead of replacing the row with it.
  return saved;
}

export async function setTaxRate(storeId: string, code: string, country: string, rate: number) {
  const normalized = taxCountry(country);
  if (!validTaxCode(code) || !normalized || !validTaxRate(rate)) throw new RangeError("Invalid tax rate.");
  const saved = checkedRate(await requestJson<unknown>(`${itemPath(storeId, code)}/rates/${normalized}`, { method: "PUT", body: { rate } }));
  if (saved.country !== normalized || saved.rate !== rate) throw invalid();
  return saved;
}

async function remove(url: string) {
  const result = await requestJson<unknown>(url, { method: "DELETE" });
  if (result !== undefined) throw invalid();
}

export async function deleteTaxClass(storeId: string, code: string) {
  if (!validTaxCode(code)) throw new RangeError("Invalid tax class code.");
  return remove(itemPath(storeId, code));
}

export async function removeTaxRate(storeId: string, code: string, country: string) {
  const normalized = taxCountry(country);
  if (!validTaxCode(code) || !normalized) throw new RangeError("Invalid tax rate.");
  return remove(`${itemPath(storeId, code)}/rates/${normalized}`);
}
