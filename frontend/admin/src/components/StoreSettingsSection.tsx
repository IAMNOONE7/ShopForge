import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { api, type AdminStore, type StoreSettings } from "../api";
import { checkedSettingsSave, storeSettingsVersion, validFreeShipping } from "../api/storeSettings";
import { isHttpError } from "../api/http";
import { fieldIssues } from "../api/errors";
import { bulkPrice, bulkPriceDecimals } from "../utils/bulkListings";
import { useAction } from "../useAction";
import { StoreThemeEditor } from "./StoreThemeEditor";
import { StoreValidationSummary } from "./StoreValidationSummary";
import {
  companyFromDraft,
  issueText,
  normalizedTheme,
  validateStoreSettings,
  type StoreIssue,
  type StoreSettingsDraft,
  type ThemeDraft,
} from "./storeValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

type Props = {
  store: AdminStore;
  reloadStores: () => void;
  canManageDomains?: boolean;
  canManage?: boolean;
  storeRefreshing?: boolean;
  storeRefreshError?: unknown | null;
};
type Failure = { error: unknown; uncertain: boolean; review: boolean; observed: AdminStore };

export function StoreSettingsSection(props: Props) {
  return <StoreSettingsEditor key={props.store.id} {...props} />;
}

function StoreSettingsEditor({ store, reloadStores, canManageDomains = false, canManage = true,
  storeRefreshing = false, storeRefreshError = null }: Props) {
  const { t } = useTranslation(["stores", "metadata", "validation"]);
  const [baseline, setBaseline] = useState({ source: store, observed: store });
  const source = baseline.source;
  const [draft, setDraft] = useState<StoreSettingsDraft>(() => settingsDraft(store));
  const [freeDraft, setFreeDraft] = useState(() => freeShippingDraft(store));
  const [issues, setIssues] = useState<StoreIssue[]>([]);
  const [saved, setSaved] = useState(false);
  const [failure, setFailure] = useState<Failure | null>(null);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(false), controller = useRef<AbortController | null>(null);
  const outcome = useRef<HTMLDivElement>(null), heading = useRef<HTMLHeadingElement>(null), latest = useRef(store);
  useLayoutEffect(() => { latest.current = store; }, [store]);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (failure) outcome.current?.focus(); }, [failure]);
  const current = !storeRefreshing && storeRefreshError === null;
  const changed = store !== baseline.observed && storeSettingsVersion(source) !== storeSettingsVersion(store);
  const denied = failure && isHttpError(failure.error) && [401, 403, 404].includes(failure.error.status ?? 0);
  const disabled = !canManage || pending || !current || changed || !!failure?.review || !!denied;
  const currency = source.status === "published" ? source.currency : draft.currency.trim().toUpperCase();
  const thresholdMessages = failure && isHttpError(failure.error) ? Object.entries(failure.error.problem?.errors ?? {})
    .filter(([field]) => field.toLowerCase() === "freeshipping.threshold")
    .flatMap(([, values]) => Array.isArray(values) ? values.filter(value => typeof value === "string" && value.trim()) : []) : [];
  const thresholdError = thresholdMessages.join(" ") || issueText(issues, "freeShipping.threshold", t);

  function reviewCurrent() {
    if (!canManage || pending || !current || denied || failure?.review && store === failure.observed) return;
    setBaseline({ source: store, observed: store }); setDraft(settingsDraft(store)); setFreeDraft(freeShippingDraft(store));
    setIssues([]); setFailure(null); setSaved(false); heading.current?.focus();
  }

  function update(field: keyof StoreSettingsDraft, value: string) {
    setSaved(false);
    if (!failure?.review) setFailure(null);
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) =>
      current.filter((issue) => issue.field !== field),
    );
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled) return;
    setSaved(false);
    const nextIssues = validateStoreSettings(draft);
    const threshold = freeDraft?.threshold.trim() ? bulkPrice(freeDraft.threshold, currency) : null;
    if (freeDraft && (freeDraft.isOn && threshold === null || freeDraft.threshold.trim() && (threshold === null || threshold <= 0)))
      nextIssues.push({ field: "freeShipping.threshold", key: "freeShippingThresholdInvalid" });
    setIssues(nextIssues);
    if (nextIssues.length > 0) {
      window.requestAnimationFrame(() => document.getElementById("store-settings-validation")?.focus());
      return;
    }
    const freeShipping = freeDraft ? { isOn: freeDraft.isOn, threshold } : undefined;
    const input: StoreSettings = {
      name: draft.name.trim(), currency, culture: draft.culture.trim(), returnWindowDays: Number(draft.returnWindowDays),
      theme: normalizedTheme(draft), company: companyFromDraft(draft),
      ...(freeShipping && (freeShipping.isOn !== source.freeShipping?.isOn || freeShipping.threshold !== source.freeShipping?.threshold) ? { freeShipping } : {}),
    };
    await run(async () => {
      setFailure(null);
      const request = new AbortController(); controller.current = request;
      try {
        const updated = checkedSettingsSave(await api.updateStore(store.id, input, request.signal), source, input);
        if (!active.current || request.signal.aborted) return;
        if (storeSettingsVersion(latest.current) !== storeSettingsVersion(store)) {
          setFailure({ error: new Error("Store settings changed during the save."), uncertain: true, review: true, observed: latest.current });
        } else {
          setBaseline({ source: updated, observed: latest.current }); setDraft(settingsDraft(updated)); setFreeDraft(freeShippingDraft(updated)); setSaved(true); heading.current?.focus();
        }
        reloadStores();
      } catch (error) {
        if (!active.current || request.signal.aborted) return;
        const refused = isHttpError(error) && error.kind === "http" && [400, 401, 403, 404, 409, 429].includes(error.status ?? 0);
        const review = !refused || isHttpError(error) && error.status === 409;
        setFailure({ error, uncertain: !refused, review, observed: latest.current });
        if (review) reloadStores();
      } finally { if (active.current && !request.signal.aborted) controller.current = null; }
    });
  }

  return (
    <section className="store-settings-panel" aria-labelledby="store-details-title">
      <div className="section-heading">
        <div>
          <h2 id="store-details-title" ref={heading} tabIndex={-1}>{t("storeDetails")}</h2>
          <p className="hint">{t("storeDetailsHint")}</p>
        </div>
        {canManageDomains && !pending && <Link to={`/stores/${store.id}/metadata`}>{t("metadata:open")}</Link>}
      </div>
      {!current && <p role="status">{t("settingsStale")}</p>}
      {changed && <p role="status">{t("settingsChanged")}</p>}
      {(!current || changed || failure?.review) && <div className="cluster">
        <Button type="button" variant="secondary" data-read-action disabled={pending || storeRefreshing} onClick={reloadStores}>{t("refreshSettings")}</Button>
        <Button type="button" variant="secondary" disabled={pending || !current || !!denied || !!failure?.review && store === failure.observed} onClick={reviewCurrent}>{t("reviewSettings")}</Button>
      </div>}
      <form className="store-settings-form" noValidate aria-busy={pending || undefined} onSubmit={save}>
        <StoreValidationSummary
          id="store-settings-validation"
          issues={issues}
        />
        <fieldset className="store-settings-groups" disabled={disabled}>
          <legend className="sr-only">{t("storeDetails")}</legend>
          <fieldset className="settings-group">
            <legend>{t("identityAndFormatting")}</legend>
            <div className="settings-fields">
              <Field
                id="name"
                name="name"
                label={t("name")}
                maxLength={200}
                value={draft.name}
                error={issueText(issues, "name", t)}
                onChange={(event) => update("name", event.target.value)}
              />
              <Field
                id="primaryHostName"
                name="primaryHostName"
                label={t("address")}
                value={store.primaryHostName ?? ""}
                readOnly
                aria-readonly="true"
                hint={<>{t("addressReadOnlyHint")} {canManageDomains && <Link to={`/stores/${store.id}/domains`}>{t("manageDomains")}</Link>}</>}
              />
              <Field
                id="currency"
                name="currency"
                label={t("currency")}
                value={
                  store.status === "published" ? store.currency : draft.currency
                }
                maxLength={3}
                readOnly={store.status === "published"}
                aria-readonly={store.status === "published" || undefined}
                hint={
                  store.status === "published"
                    ? t("publishedCurrencyHint")
                    : t("currencyHint")
                }
                error={issueText(issues, "currency", t)}
                onChange={(event) => update("currency", event.target.value)}
              />
              <Field
                id="culture"
                name="culture"
                label={t("formattingCulture")}
                value={draft.culture}
                hint={t("formattingCultureHint")}
                error={issueText(issues, "culture", t)}
                onChange={(event) => update("culture", event.target.value)}
              />
              <Field
                id="returnWindowDays"
                name="returnWindowDays"
                type="number"
                min="0"
                max="365"
                step="1"
                label={t("returnWindow")}
                value={draft.returnWindowDays}
                error={issueText(issues, "returnWindowDays", t)}
                onChange={(event) =>
                  update("returnWindowDays", event.target.value)
                }
              />
            </div>
          </fieldset>

          <fieldset className="settings-group">
            <legend>{t("companyDetails")}</legend>
            <p className="hint">{t("companyOptionalHint")}</p>
            <div className="settings-fields settings-fields-two">
              <Field
                id="legalName"
                name="legalName"
                label={t("legalName")}
                value={draft.legalName}
                error={issueText(issues, "legalName", t)}
                onChange={(event) => update("legalName", event.target.value)}
              />
              <Field
                id="line1"
                name="line1"
                label={t("street")}
                value={draft.line1}
                error={issueText(issues, "line1", t)}
                onChange={(event) => update("line1", event.target.value)}
              />
              <Field
                id="city"
                name="city"
                label={t("city")}
                value={draft.city}
                error={issueText(issues, "city", t)}
                onChange={(event) => update("city", event.target.value)}
              />
              <Field
                id="postalCode"
                name="postalCode"
                label={t("postalCode")}
                value={draft.postalCode}
                error={issueText(issues, "postalCode", t)}
                onChange={(event) => update("postalCode", event.target.value)}
              />
              <Field
                id="country"
                name="country"
                label={t("country")}
                maxLength={2}
                value={draft.country}
                hint={t("countryHint")}
                error={issueText(issues, "country", t)}
                onChange={(event) => update("country", event.target.value)}
              />
              <Field
                id="registrationNumber"
                name="registrationNumber"
                label={t("registrationNumber")}
                value={draft.registrationNumber}
                error={issueText(issues, "registrationNumber", t)}
                onChange={(event) =>
                  update("registrationNumber", event.target.value)
                }
              />
              <Field
                id="vatNumber"
                name="vatNumber"
                label={t("vatNumber")}
                value={draft.vatNumber}
                error={issueText(issues, "vatNumber", t)}
                onChange={(event) => update("vatNumber", event.target.value)}
              />
            </div>
          </fieldset>

          <fieldset className="settings-group">
            <legend>{t("freeShippingTitle")}</legend>
            {freeDraft ? <div className="settings-fields">
              <label className="listing-check settings-free-shipping-toggle"><input type="checkbox" name="freeShipping.isOn" checked={freeDraft.isOn} onChange={event => {
                setFreeDraft({ ...freeDraft, isOn: event.target.checked }); setSaved(false); setFailure(null); setIssues(current => current.filter(issue => issue.field !== "freeShipping.threshold"));
              }} />{t("freeShippingEnable")}</label>
              <Field id="freeShipping.threshold" name="freeShipping.threshold" inputMode="decimal" required={freeDraft.isOn}
                label={t("freeShippingThreshold", { currency })} hint={t("freeShippingHint", { currency, decimals: bulkPriceDecimals(currency) })}
                value={freeDraft.threshold} error={thresholdError} onChange={event => {
                  setFreeDraft({ ...freeDraft, threshold: event.target.value }); setSaved(false); setFailure(null); setIssues(current => current.filter(issue => issue.field !== "freeShipping.threshold"));
                }} />
              <p className="hint">{t("freeShippingOffHint")}</p>
            </div> : <p role="status">{t("freeShippingUnknown")}</p>}
          </fieldset>

          <fieldset className="settings-group">
            <legend>{t("theme")}</legend>
            <p className="hint">{t("themeHint")}</p>
            <StoreThemeEditor
              value={draft}
              storeName={draft.name}
              issues={issues}
              onChange={(field: keyof ThemeDraft, value) =>
                update(field, value)
              }
            />
          </fieldset>
        </fieldset>

        <div className="settings-actions">
          <Button
            type="submit"
            disabled={disabled}
            busy={pending}
            busyLabel={t("savingSettings")}
          >
            {t("saveSettings")}
          </Button>
        </div>
        {saved && !changed && current && (
          <InlineMessage tone="success">{t("settingsSaved")}</InlineMessage>
        )}
        {failure && <div ref={outcome} tabIndex={-1}>
          {failure.review && <p role="status">{t(failure.uncertain ? "settingsUncertain" : "settingsReviewRequired")}</p>}
          <RequestError error={failure.error} operation="write" fieldErrors={fieldIssues(failure.error).map(issue => ({
            field: issue.target.toLowerCase() === "freeshipping.threshold" ? "freeShipping.threshold" : issue.target,
            message: issue.target.toLowerCase() === "freeshipping.threshold" && thresholdMessages.length ? thresholdMessages.join(" ") : t(`validation:${issue.key}`),
          }))} />
        </div>}
      </form>
    </section>
  );
}

function settingsDraft(store: AdminStore): StoreSettingsDraft {
  return {
    name: store.name,
    currency: store.currency,
    culture: store.culture,
    returnWindowDays: String(store.returnWindowDays),
    primaryColor: store.theme.primaryColor,
    secondaryColor: store.theme.secondaryColor,
    borderRadius: String(store.theme.borderRadius),
    legalName: store.company?.legalName ?? "",
    line1: store.company?.line1 ?? "",
    city: store.company?.city ?? "",
    postalCode: store.company?.postalCode ?? "",
    country: store.company?.country ?? "",
    registrationNumber: store.company?.registrationNumber ?? "",
    vatNumber: store.company?.vatNumber ?? "",
  };
}

function freeShippingDraft(store: AdminStore) {
  return validFreeShipping(store.freeShipping, store.currency)
    ? { isOn: store.freeShipping.isOn, threshold: store.freeShipping.threshold === null ? "" : String(store.freeShipping.threshold) }
    : null;
}
