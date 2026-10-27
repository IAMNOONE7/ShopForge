import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type ShippingMethod } from "../api";
import { taxAssignmentValid, type TaxChoices } from "../useTaxChoices";
import { bulkPrice, bulkPriceDecimals } from "../utils/bulkListings";
import { methodVat } from "./methodValidation";
import { TaxClassField } from "./TaxClassField";
import { Button } from "./ui/Button";
import { parcelShippingApi } from "../api/parcelShipping";
import { useRequest } from "../useRequest";
import { ParcelTermsFields } from "./ParcelTermsFields";
import { parcelLimitFields, wholeNumber } from "./parcelValidation";
import { Field } from "./ui/Field";

export type ShippingInput = Parameters<typeof api.updateShippingMethod>[2] & { providerKey: string };
export function ShippingMethodForm({ method, storeId, providers, choices, disabled, pending, money, onSave, onCancel, onDirty }: {
  method?: ShippingMethod; storeId: string; onDirty?: (dirty: boolean) => void; providers: string[]; choices: TaxChoices; disabled: boolean; pending: boolean;
  money: Intl.NumberFormat; onSave: (input: ShippingInput) => Promise<void>; onCancel?: () => void;
}) {
  const { t } = useTranslation(["methods", "common", "taxAssignments", "methodSetup", "parcelShipping"]);
  const [draft, setDraft] = useState(() => ({ name: method?.name ?? "", price: method ? String(method.price) : "", vatRate: String(method?.vatRate ?? 21),
    maxWeightGrams: method?.maxWeightGrams == null ? "" : String(method.maxWeightGrams), countries: method?.countries.join(" ") ?? "",
    requiresPickupPoint: method?.requiresPickupPoint ?? false, isActive: method?.isActive ?? true, providerKey: method?.providerKey ?? "", taxClass: method?.taxClass ?? null as string | null,
    pricing: method?.pricing ?? "flat", carrierService: method?.carrierService ?? "", parcelMaxWeightGrams: method?.parcelMaxWeightGrams == null ? "" : String(method.parcelMaxWeightGrams),
    parcelLengthMm: method?.parcelLengthMm == null ? "" : String(method.parcelLengthMm), parcelWidthMm: method?.parcelWidthMm == null ? "" : String(method.parcelWidthMm), parcelHeightMm: method?.parcelHeightMm == null ? "" : String(method.parcelHeightMm) }));
  const [mode, setMode] = useState(method ? method.requiresPickupPoint ? "box" : "address" : ""), [invalid, setInvalid] = useState<string[]>([]);
  const heading = useRef<HTMLHeadingElement>(null); useEffect(() => { heading.current?.focus(); }, []);
  const id = `shipping-${method?.code ?? "new"}`, providerKey = method?.providerKey ?? draft.providerKey;
  const [initial] = useState(() => JSON.stringify({ draft, mode }));
  const dirty = initial !== JSON.stringify({ draft, mode });
  useEffect(() => { onDirty?.(dirty); }, [dirty, onDirty]);
  const perParcel = draft.pricing === "perParcel";
  const [services, reloadServices] = useRequest("shipping-services:" + storeId + ":" + draft.pricing, signal => perParcel ? parcelShippingApi.services(storeId, signal) : Promise.resolve([]));
  const selectedService = services.status === "ready" ? services.data.find(service => service.key === draft.carrierService && service.providerKey === providerKey && service.isActive) : undefined;
  const servicesFresh = services.status === "ready" && !services.refreshing && !services.refreshError;
  const termsChanged = !method || draft.pricing !== (method.pricing ?? "flat") || draft.carrierService !== (method.carrierService ?? "")
    || parcelLimitFields.some(field => draft[field] !== (method[field] == null ? "" : String(method[field])));
  const currency = money.resolvedOptions().currency ?? "";
  const taxValid = taxAssignmentValid(draft.taxClass, choices, method?.taxClass ?? null);
  function update<K extends keyof typeof draft>(field: K, value: typeof draft[K]) { setDraft(current => ({ ...current, [field]: value })); setInvalid(current => current.filter(candidate => candidate !== field)); }
  function error(field: "name" | "price" | "vatRate" | "maxWeightGrams" | "countries") { return invalid.includes(field) ? t(`methodSetup:validation.${field}`, { decimals: bulkPriceDecimals(currency) }) : undefined; }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled || pending) return;
    const price = perParcel ? method?.price ?? 0 : bulkPrice(draft.price, currency), vatRate = methodVat(draft.vatRate);
    const countries = [...new Set(draft.countries.toUpperCase().split(/[\s,]+/).filter(Boolean))];
    const weight = perParcel ? "" : draft.maxWeightGrams.trim(), maxWeightGrams = weight === "" ? null : Number(weight);
    const limits = Object.fromEntries(parcelLimitFields.map(field => [field, perParcel && draft[field].trim() ? wholeNumber(draft[field], field === "parcelMaxWeightGrams" ? 2147483647 : 10000) : null]));
    const sides = [draft.parcelLengthMm, draft.parcelWidthMm, draft.parcelHeightMm].filter(value => value.trim());
    const issues = [!draft.name.trim() || draft.name.trim().length > 100 ? "name" : "", !method && !providers.includes(providerKey) ? "providerKey" : "",
      price === null ? "price" : "", vatRate === null ? "vatRate" : "", !taxValid ? "taxClass" : "",
      weight !== "" && (!/^\d+$/.test(weight) || !Number.isInteger(maxWeightGrams) || maxWeightGrams! <= 0 || maxWeightGrams! > 2147483647) ? "maxWeightGrams" : "",
      countries.some(country => !/^[A-Z]{2}$/.test(country)) ? "countries" : "", !perParcel && providerKey === "packeta" && !["box", "address"].includes(mode) ? "requiresPickupPoint" : "", perParcel && termsChanged && (!servicesFresh || !selectedService) ? "carrierService" : "",
      ...parcelLimitFields.filter(field => perParcel && draft[field].trim() && limits[field] === null),
      ...(perParcel && sides.length > 0 && sides.length !== 3 ? ["parcelLengthMm"] : []),
      ...(perParcel && termsChanged && selectedService && limits.parcelMaxWeightGrams && limits.parcelMaxWeightGrams > selectedService.maxWeightGrams ? ["parcelMaxWeightGrams"] : [])].filter(Boolean);
    setInvalid(issues); if (issues.length) { document.getElementById(`${id}-${issues[0]}`)?.focus(); return; }
    await onSave({ name: draft.name.trim(), price: price!, vatRate: vatRate!, maxWeightGrams, countries,
      requiresPickupPoint: perParcel ? termsChanged && selectedService ? selectedService.kind !== "home" : method?.requiresPickupPoint ?? false : providerKey === "packeta" ? mode === "box" : draft.requiresPickupPoint, isActive: draft.isActive, providerKey, taxClass: draft.taxClass,
      ...(termsChanged && (perParcel || method?.pricing === "perParcel") ? { pricing: draft.pricing, carrierService: perParcel ? draft.carrierService : null, ...limits } : {}) });
  }
  return <form className="shipping-method-form method-editor" aria-busy={pending} aria-labelledby={`${id}-title`} noValidate onSubmit={submit} onKeyDown={event => { if (event.key === "Escape" && !pending && onCancel) { event.preventDefault(); onCancel(); } }}>
    <h3 id={`${id}-title`} ref={heading} tabIndex={-1}>{method?.name ?? t("methods:newShipping")}</h3><fieldset className="shipping-method-fields" disabled={disabled || pending}>
      <legend className="sr-only">{method?.name ?? t("methods:newShipping")}</legend>
      <Field id={`${id}-name`} name="name" label={t("taxAssignments:methodName")} hint={t("methodSetup:nameHint")} value={draft.name} error={error("name")} onChange={event => update("name", event.target.value)} />
      {method ? <p className="hint">{t("methodSetup:immutable", { code: method.code, provider: method.providerKey })}</p> : <div className="listing-field"><label htmlFor={`${id}-providerKey`}>{t("taxAssignments:provider")}</label><select id={`${id}-providerKey`} name="providerKey" value={providerKey} aria-describedby={invalid.includes("providerKey") ? `${id}-provider-error` : undefined} aria-invalid={invalid.includes("providerKey") || undefined} onChange={event => update("providerKey", event.target.value)}>
        <option value="" disabled>{t("taxAssignments:chooseProvider")}</option>{providers.map(key => <option key={key} value={key}>{key}</option>)}
      </select>{invalid.includes("providerKey") && <p className="field-error" id={`${id}-provider-error`}>{t("methodSetup:invalidProvider")}</p>}</div>}
      <div className="listing-field"><label htmlFor={id + "-pricing"}>{t("parcelShipping:pricing")}</label><select id={id + "-pricing"} name="pricing" value={draft.pricing} onChange={event => update("pricing", event.target.value as "flat" | "perParcel")}><option value="flat">{t("parcelShipping:flat")}</option><option value="perParcel">{t("parcelShipping:perParcel")}</option></select></div>
      {method?.pricing === "perParcel" && !perParcel && <p>{t("parcelShipping:clearRatesWarning")}</p>}
      {perParcel && <ParcelTermsFields id={id} draft={draft} provider={providerKey} state={services} reload={reloadServices} invalid={invalid} update={(values, service) => { setDraft(current => ({ ...current, ...values, ...(service ? { countries: service.country } : {}) })); setInvalid([]); }} />}
      {!perParcel && providerKey === "packeta" && <div className="listing-field"><label htmlFor={`${id}-requiresPickupPoint`}>{t("methodSetup:deliveryMode")}</label><select id={`${id}-requiresPickupPoint`} name="requiresPickupPoint" value={mode} aria-invalid={invalid.includes("requiresPickupPoint") || undefined} aria-describedby={`${id}-mode-hint${invalid.includes("requiresPickupPoint") ? ` ${id}-mode-error` : ""}`} onChange={event => setMode(event.target.value)}>
        <option value="" disabled>{t("methodSetup:chooseMode")}</option><option value="box">{t("methodSetup:zbox")}</option><option value="address">{t("methodSetup:address")}</option>
      </select><p className="hint" id={`${id}-mode-hint`}>{t(mode === "box" ? "methodSetup:zboxHint" : mode === "address" ? "methodSetup:addressHint" : "methodSetup:modeHint")}</p>
        {invalid.includes("requiresPickupPoint") && <p className="field-error" id={`${id}-mode-error`}>{t("methodSetup:invalidMode")}</p>}</div>}
      <div className="shipping-method-row">{!perParcel && <Field id={`${id}-price`} name="price" label={t("methods:price")} inputMode="decimal" value={draft.price} error={error("price")} hint={t("methodSetup:priceHint", { currency, decimals: bulkPriceDecimals(currency) })} onChange={event => update("price", event.target.value)} />}
        <Field id={`${id}-vatRate`} name="vatRate" label={t("methods:vatPercent")} inputMode="decimal" value={draft.vatRate} error={error("vatRate")} onChange={event => update("vatRate", event.target.value)} /></div>
      <TaxClassField id={`${id}-taxClass`} choices={choices} value={draft.taxClass} saved={method?.taxClass ?? null} disabled={disabled} invalid={invalid.includes("taxClass")} onChange={value => update("taxClass", value)} />
      <div className="shipping-method-row">{!perParcel && <Field id={`${id}-maxWeightGrams`} name="maxWeightGrams" label={t("methods:maxWeight")} type="number" min="1" step="1" value={draft.maxWeightGrams} error={error("maxWeightGrams")} placeholder={t("methods:noLimit")} onChange={event => update("maxWeightGrams", event.target.value)} />}
        <Field id={`${id}-countries`} name="countries" readOnly={perParcel} label={t("methods:countries")} value={draft.countries} error={error("countries")} hint={t(providerKey === "packeta" && mode === "box" ? "methodSetup:zboxCountries" : "methodSetup:countriesHint")} placeholder={t("methods:everywhere")} onChange={event => update("countries", event.target.value)} /></div>
      {!perParcel && providerKey !== "packeta" && <label className="listing-check"><input id={`${id}-requiresPickupPoint`} name="requiresPickupPoint" type="checkbox" checked={draft.requiresPickupPoint} onChange={event => update("requiresPickupPoint", event.target.checked)} />{t("methods:needsPickup")}</label>}
      {method ? <label className="listing-check"><input id={`${id}-isActive`} name="isActive" type="checkbox" checked={draft.isActive} onChange={event => update("isActive", event.target.checked)} />{t(perParcel ? "methodSetup:active" : "methods:offered")}</label> : <p className="hint">{t("methodSetup:newActive")}</p>}
    </fieldset><div className="cluster"><Button type="submit" disabled={disabled || !taxValid} busy={pending} busyLabel={t("taxAssignments:saving")}>{t(method ? "common:save" : "common:add")}</Button>
      {onCancel && <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("methodSetup:cancel")}</Button>}</div>
  </form>;
}
