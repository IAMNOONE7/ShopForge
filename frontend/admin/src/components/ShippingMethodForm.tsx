import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type ShippingMethod } from "../api";
import { taxAssignmentValid, type TaxChoices } from "../useTaxChoices";
import { TaxClassField } from "./TaxClassField";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export type ShippingInput = Parameters<typeof api.updateShippingMethod>[2] & { providerKey: string };

export function ShippingMethodForm({ method, providers, choices, disabled, pending, money, onSave }: {
  method?: ShippingMethod; providers: string[]; choices: TaxChoices; disabled: boolean; pending: boolean;
  money: Intl.NumberFormat; onSave: (input: ShippingInput) => Promise<void>;
}) {
  const { t } = useTranslation(["methods", "common", "taxAssignments"]);
  const [draft, setDraft] = useState(() => ({ name: method?.name ?? "", price: method ? String(method.price) : "",
    vatRate: String(method?.vatRate ?? 21), maxWeightGrams: method?.maxWeightGrams == null ? "" : String(method.maxWeightGrams),
    countries: method?.countries.join(" ") ?? "", requiresPickupPoint: method?.requiresPickupPoint ?? false,
    isActive: method?.isActive ?? true, providerKey: method?.providerKey ?? "",
    taxClass: method?.taxClass ?? null as string | null }));
  const [taxInvalid, setTaxInvalid] = useState(false);
  const id = `shipping-${method?.code ?? "new"}`;
  const taxValid = taxAssignmentValid(draft.taxClass, choices, method?.taxClass ?? null);
  const providerKey = draft.providerKey || (providers.includes("manual") ? "manual" : "");
  function update<K extends keyof typeof draft>(field: K, value: typeof draft[K]) { setDraft(current => ({ ...current, [field]: value })); }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled || !event.currentTarget.checkValidity()) return;
    if (!taxValid) { setTaxInvalid(true); document.getElementById(`${id}-taxClass`)?.focus(); return; }
    if (!method && !providers.includes(providerKey)) return;
    await onSave({ name: draft.name, price: Number(draft.price), vatRate: Number(draft.vatRate),
      maxWeightGrams: draft.maxWeightGrams.trim() === "" ? null : Number(draft.maxWeightGrams),
      countries: draft.countries.toUpperCase().split(/[\s,]+/).filter(Boolean),
      requiresPickupPoint: draft.requiresPickupPoint, isActive: draft.isActive, providerKey, taxClass: draft.taxClass });
  }
  return <form className="shipping-method-form" aria-labelledby={`${id}-title`} onSubmit={submit}>
    <h4 id={`${id}-title`}>{method?.name ?? t("methods:newShipping")}</h4>
    <fieldset className="shipping-method-fields" disabled={disabled}>
      <legend className="sr-only">{method?.name ?? t("methods:newShipping")}</legend>
      <Field id={`${id}-name`} name="name" label={t("taxAssignments:methodName")} value={draft.name} required maxLength={100} onChange={event => update("name", event.target.value)} />
      {method ? <p className="hint"><code>{method.code}</code> · {method.providerKey} · {money.format(method.price)}</p>
        : <div className="listing-field"><label htmlFor={`${id}-providerKey`}>{t("taxAssignments:provider")}</label><select id={`${id}-providerKey`} name="providerKey" value={providerKey} required onChange={event => update("providerKey", event.target.value)}>
          <option value="" disabled>{t("taxAssignments:chooseProvider")}</option>{providers.map(key => <option key={key} value={key}>{key}</option>)}
        </select></div>}
      <div className="shipping-method-row">
        <Field id={`${id}-price`} name="price" label={t("methods:price")} type="number" min="0" step="0.01" value={draft.price} required onChange={event => update("price", event.target.value)} />
        <Field id={`${id}-vatRate`} name="vatRate" label={t("methods:vatPercent")} type="number" min="0" max="100" step="0.01" value={draft.vatRate} required onChange={event => update("vatRate", event.target.value)} />
      </div>
      <TaxClassField id={`${id}-taxClass`} choices={choices} value={draft.taxClass} saved={method?.taxClass ?? null} disabled={disabled} invalid={taxInvalid}
        onChange={value => { update("taxClass", value); setTaxInvalid(false); }} />
      <div className="shipping-method-row">
        <Field id={`${id}-maxWeightGrams`} name="maxWeightGrams" label={t("methods:maxWeight")} type="number" min="1" step="1" value={draft.maxWeightGrams} placeholder={t("methods:noLimit")} onChange={event => update("maxWeightGrams", event.target.value)} />
        <Field id={`${id}-countries`} name="countries" label={t("methods:countries")} value={draft.countries} placeholder={t("methods:everywhere")} onChange={event => update("countries", event.target.value)} />
      </div>
      <label className="listing-check"><input name="requiresPickupPoint" type="checkbox" checked={draft.requiresPickupPoint} onChange={event => update("requiresPickupPoint", event.target.checked)} />{t("methods:needsPickup")}</label>
      {method && <label className="listing-check"><input name="isActive" type="checkbox" checked={draft.isActive} onChange={event => update("isActive", event.target.checked)} />{t("methods:offered")}</label>}
    </fieldset>
    <Button type="submit" disabled={disabled || !taxValid || !method && !providers.includes(providerKey)} busy={pending} busyLabel={t("taxAssignments:saving")}>{t(method ? "common:save" : "common:add")}</Button>
  </form>;
}
