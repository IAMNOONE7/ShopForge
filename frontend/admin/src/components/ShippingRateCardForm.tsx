import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { ShippingMethod } from "../api";
import { calendarDate, type RateCard, type RateSource } from "../api/parcelShipping";
import { bulkPrice, bulkPriceDecimals } from "../utils/bulkListings";
import { wholeNumber } from "./parcelValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export function ShippingRateCardForm({ method, money, disabled, pending, onDirty, onSave }: {
  method: ShippingMethod; money: Intl.NumberFormat; disabled: boolean; pending: boolean; onDirty: (dirty: boolean) => void; onSave: (card: RateCard) => Promise<void>;
}) {
  const { t } = useTranslation("parcelShipping");
  const counter = useRef(50);
  const saved = () => ({ source: method.ratesFrom ?? "", pricedOn: method.ratesPricedOn ?? "", bands: (method.rateBands?.length ? method.rateBands : [{ upToGrams: "", price: "" }]).map((band, index) => ({ id: index + 1, upToGrams: String(band.upToGrams), price: String(band.price) })) });
  const [draft, setDraft] = useState(saved);
  const [initial] = useState(() => JSON.stringify({ source: draft.source, pricedOn: draft.pricedOn, bands: draft.bands.map(({ id: _id, ...band }) => band) }));
  const [issues, setIssues] = useState<string[]>([]), summary = useRef<HTMLDivElement>(null);
  const dirty = initial !== JSON.stringify({ source: draft.source, pricedOn: draft.pricedOn, bands: draft.bands.map(({ id: _id, ...band }) => band) });
  useEffect(() => { onDirty(dirty); }, [dirty, onDirty]);
  const currency = money.resolvedOptions().currency ?? "", id = "shipping-" + method.code;
  async function submit(event: FormEvent) {
    event.preventDefault(); if (disabled || pending) return;
    const bands = draft.bands.map(band => ({ upToGrams: wholeNumber(band.upToGrams), price: bulkPrice(band.price, currency) }));
    const invalid = [!["carrier", "own", "placeholder"].includes(draft.source) ? "source" : "", (draft.source === "carrier" || draft.pricedOn) && !calendarDate(draft.pricedOn) ? "pricedOn" : "",
      ...bands.flatMap((band, index) => [band.upToGrams === null || index > 0 && band.upToGrams <= (bands[index - 1].upToGrams ?? 0) ? "weight-" + draft.bands[index].id : "", band.price === null ? "price-" + draft.bands[index].id : ""])].filter(Boolean);
    setIssues(invalid); if (invalid.length) { window.requestAnimationFrame(() => summary.current?.focus()); return; }
    await onSave({ source: draft.source as RateSource, pricedOn: draft.pricedOn || null, bands: bands as RateCard["bands"] });
  }
  function reset() { setDraft(saved()); setIssues([]); }
  return <form className="parcel-rate-form method-editor" aria-label={t("rateEditor", { name: method.name })} onSubmit={event => void submit(event)} noValidate>
    <h3>{t("rateCard")}</h3><p>{t("separateSaves")}</p><p>{t("placeholderWarning")}</p>
    {disabled && !pending && <p role="status">{t("saveTermsFirst")}</p>}
    {issues.length > 0 && <div ref={summary} tabIndex={-1} role="alert"><p>{t("checkCard")}</p><ul>{issues.map(issue => <li key={issue}><button className="link-button" type="button" onClick={() => document.getElementById(id + "-" + issue)?.focus()}>{t(issue === "source" ? "sourceInvalid" : issue === "pricedOn" ? "dateInvalid" : issue.startsWith("weight") ? "bandWeightInvalid" : "bandPriceInvalid", { decimals: bulkPriceDecimals(currency), currency })}</button></li>)}</ul></div>}
    <fieldset disabled={disabled || pending}><legend className="sr-only">{t("rateCard")}</legend>
      <label htmlFor={id + "-source"}>{t("source")}</label><select id={id + "-source"} name="source" value={draft.source} aria-invalid={issues.includes("source") || undefined} onChange={event => { setDraft({ ...draft, source: event.target.value as RateSource }); setIssues([]); }}><option value="">{t("chooseSource")}</option>{(["carrier", "own", "placeholder"] as const).map(source => <option key={source} value={source}>{t(source)}</option>)}</select>
      <Field id={id + "-pricedOn"} name="pricedOn" label={t("pricedOn")} type="date" hint={t("dateHint")} value={draft.pricedOn} error={issues.includes("pricedOn") ? t("dateInvalid") : undefined} onChange={event => { setDraft({ ...draft, pricedOn: event.target.value }); setIssues([]); }} />
      <div id={id + "-bands"} tabIndex={-1} className="parcel-bands"><p>{t("bandsHint", { currency, decimals: bulkPriceDecimals(currency) })}</p>
        {draft.bands.map((band, index) => <div className="parcel-band" key={band.id}><h4>{t("band", { number: index + 1 })}</h4>
          <Field id={id + "-weight-" + band.id} label={t("bandWeight", { number: index + 1 })} inputMode="numeric" value={band.upToGrams} error={issues.includes("weight-" + band.id) ? t("bandWeightInvalid") : undefined} onChange={event => { setDraft({ ...draft, bands: draft.bands.map(item => item.id === band.id ? { ...item, upToGrams: event.target.value } : item) }); setIssues([]); }} />
          <Field id={id + "-price-" + band.id} label={t("bandPrice", { number: index + 1, currency })} inputMode="decimal" value={band.price} error={issues.includes("price-" + band.id) ? t("bandPriceInvalid", { currency, decimals: bulkPriceDecimals(currency) }) : undefined} onChange={event => { setDraft({ ...draft, bands: draft.bands.map(item => item.id === band.id ? { ...item, price: event.target.value } : item) }); setIssues([]); }} />
          <Button type="button" variant="secondary" disabled={draft.bands.length <= 1} onClick={() => { setDraft({ ...draft, bands: draft.bands.filter(item => item.id !== band.id) }); setIssues([]); }}>{t("removeBand", { number: index + 1 })}</Button>
        </div>)}
      </div><Button type="button" variant="secondary" disabled={draft.bands.length >= 50} onClick={() => setDraft({ ...draft, bands: [...draft.bands, { id: ++counter.current, upToGrams: "", price: "" }] })}>{t("addBand")}</Button>
    </fieldset>
    <div className="cluster"><Button type="submit" disabled={disabled} busy={pending} busyLabel={t("savingRates")}>{t("saveRates")}</Button><Button type="button" variant="secondary" disabled={pending || disabled} onClick={reset}>{t("resetRates")}</Button></div>
  </form>;
}
