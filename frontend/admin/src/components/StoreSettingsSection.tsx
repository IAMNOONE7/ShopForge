import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type AdminStore, type StoreSettings } from "../api";
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

export function StoreSettingsSection({
  store,
  reloadStores,
}: {
  store: AdminStore;
  reloadStores: () => void;
}) {
  const { t } = useTranslation("stores");
  const [draft, setDraft] = useState<StoreSettingsDraft>(() =>
    settingsDraft(store),
  );
  const [issues, setIssues] = useState<StoreIssue[]>([]);
  const [saved, setSaved] = useState(false);
  const [error, run, pending] = useAction(reloadStores);

  function update(field: keyof StoreSettingsDraft, value: string) {
    setSaved(false);
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) =>
      current.filter((issue) => issue.field !== field),
    );
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const nextIssues = validateStoreSettings(draft);
    setIssues(nextIssues);
    if (nextIssues.length > 0) {
      window.requestAnimationFrame(() =>
        document.getElementById("store-settings-validation")?.focus(),
      );
      return;
    }

    const input: StoreSettings = {
      name: draft.name.trim(),
      currency:
        store.status === "published"
          ? store.currency
          : draft.currency.trim().toUpperCase(),
      culture: draft.culture.trim(),
      returnWindowDays: Number(draft.returnWindowDays),
      theme: normalizedTheme(draft),
      company: companyFromDraft(draft),
    };
    await run(async () => {
      const updated = await api.updateStore(store.id, input);
      setDraft(settingsDraft(updated));
      setSaved(true);
    });
  }

  return (
    <section className="store-settings-panel" aria-labelledby="store-details-title">
      <div className="section-heading">
        <div>
          <h2 id="store-details-title">{t("storeDetails")}</h2>
          <p className="hint">{t("storeDetailsHint")}</p>
        </div>
      </div>
      <form className="store-settings-form" noValidate onSubmit={save}>
        <StoreValidationSummary
          id="store-settings-validation"
          issues={issues}
        />
        <div className="store-settings-groups">
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
                hint={t("addressReadOnlyHint")}
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
        </div>

        <div className="settings-actions">
          <Button
            type="submit"
            busy={pending}
            busyLabel={t("savingSettings")}
          >
            {t("saveSettings")}
          </Button>
        </div>
        {saved && (
          <InlineMessage tone="success">{t("settingsSaved")}</InlineMessage>
        )}
        {error !== null && <RequestError error={error} operation="write" />}
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
