import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useOutletContext } from "react-router";
import { api } from "../api";
import { fieldIssues } from "../api/errors";
import { StoreThemeEditor } from "../components/StoreThemeEditor";
import { StoreValidationSummary } from "../components/StoreValidationSummary";
import {
  issueText,
  normalizedTheme,
  normalizedStoreAddress,
  storeAddressIssue,
  validateCreateStore,
  type CreateStoreDraft,
  type StoreIssue,
  type ThemeDraft,
} from "../components/storeValidation";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { RequestError } from "../components/ui/RequestError";
import { useAction } from "../useAction";

type LayoutContext = { reloadStores: () => void };

const initialDraft: CreateStoreDraft = {
  name: "",
  hostName: "",
  currency: "CZK",
  culture: "cs-CZ",
  primaryColor: "#1F6FEB",
  secondaryColor: "#EEF4FF",
  borderRadius: "6",
};

export function NewStorePage() {
  const { t } = useTranslation(["stores", "errors", "validation"]);
  const navigate = useNavigate();
  const { reloadStores } = useOutletContext<LayoutContext>();
  const [draft, setDraft] = useState<CreateStoreDraft>(initialDraft);
  const [issues, setIssues] = useState<StoreIssue[]>([]);
  const [error, run, pending] = useAction(reloadStores);
  const active = useRef(true);
  useEffect(() => {
    active.current = true;
    return () => { active.current = false; };
  }, []);
  const addressIssue = storeAddressIssue(error);
  const requestFields = addressIssue ? [
    ...fieldIssues(error)
      .filter((issue) => issue.target !== "hostName")
      .map((issue) => ({ field: issue.target, message: t(`validation:${issue.key}`) })),
    { field: "hostName", message: t(`stores:validation.${addressIssue.key}`) },
  ] : undefined;

  function update(field: keyof CreateStoreDraft, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) =>
      current.filter((issue) => issue.field !== field),
    );
  }

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending) return;
    const nextIssues = validateCreateStore(draft);
    setIssues(nextIssues);
    if (nextIssues.length > 0) {
      window.requestAnimationFrame(() =>
        document.getElementById("new-store-validation")?.focus(),
      );
      return;
    }
    await run(async () => {
      const store = await api.createStore({
        name: draft.name.trim(),
        hostName: normalizedStoreAddress(draft.hostName),
        currency: draft.currency.trim().toUpperCase(),
        culture: draft.culture.trim(),
        theme: normalizedTheme(draft),
      });
      if (active.current) void navigate(`/stores/${store.id}/settings`);
    });
  }

  return (
    <div className="new-store-page">
      <div className="page-heading-row new-store-heading">
        <div>
          <h1>{t("stores:newTitle")}</h1>
          <p className="hint">{t("stores:newHint")}</p>
        </div>
      </div>
      <form className="new-store-form" noValidate onSubmit={create}>
        <StoreValidationSummary id="new-store-validation" issues={issues} />
        <div className="new-store-layout">
          <fieldset className="settings-group" disabled={pending}>
            <legend>{t("stores:identityAndFormatting")}</legend>
            <div className="settings-fields">
              <Field
                id="name"
                name="name"
                label={t("stores:name")}
                maxLength={200}
                autoFocus
                value={draft.name}
                error={issueText(issues, "name", t)}
                onChange={(event) => update("name", event.target.value)}
              />
              <Field
                id="hostName"
                name="hostName"
                label={t("stores:addressName")}
                placeholder="wooden-home"
                autoCapitalize="none"
                spellCheck={false}
                autoComplete="off"
                value={draft.hostName}
                hint={t("stores:newAddressHint")}
                error={issueText(issues, "hostName", t) ?? (addressIssue ? t(`stores:validation.${addressIssue.key}`) : undefined)}
                onChange={(event) => update("hostName", event.target.value)}
              />
              <div className="settings-fields settings-fields-two">
                <Field
                  id="currency"
                  name="currency"
                  label={t("stores:currency")}
                  maxLength={3}
                  value={draft.currency}
                  readOnly
                  hint={t("stores:newCurrencyHint")}
                  error={issueText(issues, "currency", t)}
                />
                <Field
                  id="culture"
                  name="culture"
                  label={t("stores:formattingCulture")}
                  value={draft.culture}
                  hint={t("stores:formattingCultureHint")}
                  error={issueText(issues, "culture", t)}
                  onChange={(event) => update("culture", event.target.value)}
                />
              </div>
            </div>
          </fieldset>

          <fieldset className="settings-group" disabled={pending}>
            <legend>{t("stores:theme")}</legend>
            <p className="hint">{t("stores:themeHint")}</p>
            <StoreThemeEditor
              value={draft}
              storeName={draft.name}
              previewPrice={t("stores:newPreviewPrice")}
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
            busyLabel={t("stores:creatingStore")}
          >
            {t("stores:create")}
          </Button>
        </div>
        {error !== null && <RequestError error={error} operation="write" fieldErrors={requestFields} />}
      </form>
    </div>
  );
}
