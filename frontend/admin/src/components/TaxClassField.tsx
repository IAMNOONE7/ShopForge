import { useTranslation } from "react-i18next";
import { taxChoicesReady, type TaxChoices } from "../useTaxChoices";
import { uiLocale } from "../utils/format";
import { Button } from "./ui/Button";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

export function TaxClassField({ id, choices, value, saved, onChange, disabled, invalid = false }: {
  id: string; choices: TaxChoices; value: string | null | undefined; saved?: string | null;
  onChange: (value: string | null) => void; disabled: boolean; invalid?: boolean;
}) {
  const { t, i18n } = useTranslation("taxAssignments");
  const { state, reload } = choices;
  const ready = taxChoicesReady(state);
  const items = state.status === "ready" ? state.data.items : [];
  const selected = items.find(item => item.code === value);
  const unavailable = typeof value === "string" && !selected;
  const error = state.status === "error" || state.status === "not-found" ? state.error : state.status === "ready" ? state.refreshError : null;
  return <div className="tax-assignment-field">
    <label htmlFor={id}>{t("label")}</label>
    <select id={id} name="taxClass" value={value === undefined ? "?" : value ?? ""} disabled={disabled}
      aria-invalid={invalid || undefined} aria-describedby={`${id}-hint${invalid ? ` ${id}-error` : ""}`}
      onChange={event => { if (event.target.value !== "?") onChange(event.target.value || null); }}>
      {value === undefined && <option value="?" disabled>{t("choose")}</option>}
      <option value="">{t("fallback")}</option>
      {unavailable && <option value={value} disabled={value !== saved}>{t("unavailableOption", { code: value })}</option>}
      {items.map(item => <option key={item.code} value={item.code} disabled={!ready && item.code !== saved}>{item.name} ({item.code})</option>)}
    </select>
    <p id={`${id}-hint`} className="hint">{t("hint")}</p>
    {state.status === "loading" && <LoadingState label={t("loading")} lines={1} />}
    {state.status === "ready" && state.refreshing && <p role="status">{t("loading")}</p>}
    {error !== null && <><RequestError error={error} operation="read" onRetry={reload} /><p className="hint">{t("lookupFailed")}</p></>}
    {ready && unavailable && <p className="hint">{t(value === saved ? "savedUnavailable" : "choiceUnavailable", { code: value })}</p>}
    {selected && ready && <p className="hint">{t("rates", { rates: selected.rates.length ? selected.rates.map(rate => `${rate.country} ${new Intl.NumberFormat(uiLocale(i18n.resolvedLanguage), { maximumFractionDigits: 2 }).format(rate.rate)}%`).join(" · ") : t("noRates") })}</p>}
    {invalid && <p id={`${id}-error`} className="field-error" role="alert">{t("invalid")}</p>}
    <Button type="button" variant="secondary" disabled={disabled || state.status === "loading" || state.status === "ready" && state.refreshing} onClick={reload}>{t("refresh")}</Button>
  </div>;
}
