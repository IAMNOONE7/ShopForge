import { parcelLimitFields } from "./parcelValidation";
import { useTranslation } from "react-i18next";
import type { CarrierService } from "../api/parcelShipping";
import type { RequestState } from "../useRequest";
import { RequestError } from "./ui/RequestError";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export type ParcelDraft = { carrierService: string; parcelMaxWeightGrams: string; parcelLengthMm: string; parcelWidthMm: string; parcelHeightMm: string };
export function ParcelTermsFields({ id, draft, provider, state, reload, invalid, update }: {
  id: string; draft: ParcelDraft; provider: string; state: RequestState<CarrierService[]>; reload: () => void; invalid: string[];
  update: (values: Partial<ParcelDraft>, service?: CarrierService) => void;
}) {
  const { t } = useTranslation("parcelShipping");
  const fresh = state.status === "ready" && !state.refreshing && !state.refreshError;
  const offered = state.status === "ready" ? state.data.filter(service => service.isActive && service.providerKey === provider) : [];
  const selected = offered.find(service => service.key === draft.carrierService);
  const error = state.status === "ready" ? state.refreshError : state.status === "loading" ? null : state.error;
  return <div className="parcel-terms">
    <label htmlFor={id + "-carrierService"}>{t("service")}</label>
    <select id={id + "-carrierService"} name="carrierService" value={draft.carrierService} aria-invalid={invalid.includes("carrierService") || undefined} aria-describedby={id + "-service-hint"} onChange={event => {
      const service = offered.find(item => item.key === event.target.value); update({ carrierService: event.target.value }, service);
    }}>
      <option value="">{t("chooseService")}</option>
      {draft.carrierService && !selected && <option value={draft.carrierService}>{draft.carrierService} — {t("unoffered")}</option>}
      {offered.map(service => <option key={service.key} value={service.key} disabled={!fresh}>{service.name} · {service.country}</option>)}
    </select>
    <p id={id + "-service-hint"} className="hint">{t(selected ? "serviceHint" : "serviceMissing")}</p>
    {invalid.includes("carrierService") && <p className="field-error">{t("serviceInvalid")}</p>}
    {!fresh && <p role="status">{t("servicesStale")}</p>}
    {error !== null && <RequestError error={error} operation="read" onRetry={reload} />}
    <Button type="button" variant="secondary" disabled={state.status === "loading" || state.status === "ready" && state.refreshing} onClick={reload}>{t("refreshServices")}</Button>
    {selected && <dl className="parcel-service-facts"><div><dt>{t("kind")}</dt><dd>{t(selected.kind)}</dd></div><div><dt>{t("serviceWeight")}</dt><dd>{selected.maxWeightGrams} g</dd></div><div><dt>{t("largest")}</dt><dd>{selected.largest.lengthMm} × {selected.largest.widthMm} × {selected.largest.heightMm} mm</dd></div><div><dt>{t("smallest")}</dt><dd>{selected.smallest.lengthMm} × {selected.smallest.widthMm} × {selected.smallest.heightMm} mm</dd></div><div><dt>{t("sum")}</dt><dd>{selected.maxSumOfSidesMm ?? "—"}</dd></div><div><dt>{t("volume")}</dt><dd>{selected.largestVolumeMm3} mm³</dd></div><div><dt>{t("parcelsLimit")}</dt><dd>{selected.maxParcels ?? "—"}</dd></div><div><dt>{t("serviceSource")}</dt><dd>{selected.source} · {selected.sourceDate}</dd></div></dl>}
    <p>{t("limitsHint")}</p>
    <div className="parcel-limit-fields">{parcelLimitFields.map(field => <Field key={field} id={id + "-" + field} name={field} label={t(field)} inputMode="numeric" value={draft[field]} error={invalid.includes(field) ? t("limitInvalid") : undefined} onChange={event => update({ [field]: event.target.value })} />)}</div>
  </div>;
}
