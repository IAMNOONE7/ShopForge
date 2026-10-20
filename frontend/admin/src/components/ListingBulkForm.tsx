import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api } from "../api";
import type { BulkListingIntent } from "../useBulkListings";
import { useRequest } from "../useRequest";
import { taxAssignmentValid, useTaxChoices } from "../useTaxChoices";
import { TaxClassField } from "./TaxClassField";
import { bulkChange, bulkPercent, bulkPrice, bulkPriceDecimals, type BulkListingAction } from "../utils/bulkListings";
import { categoryPath } from "./categoryTree";
import { ListingRead } from "./ListingEditor";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { InlineMessage } from "./ui/InlineMessage";

const activeActions: BulkListingAction[] = ["hide", "show", "setPrice", "changePrice", "categories", "taxClass", "archive"];

export function ListingBulkForm({ storeId, currency, archived, disabled, hidden, revision, onReview }: {
  storeId: string; currency: string; archived: boolean; disabled: boolean; hidden: boolean; revision: number; onReview: (intent: BulkListingIntent) => void;
}) {
  const { t } = useTranslation("bulkListings");
  const [action, setAction] = useState<BulkListingAction>(archived ? "restore" : "hide");
  const [value, setValue] = useState("");
  const [categoryActions, setCategoryActions] = useState<Record<string, "add" | "remove">>({});
  const [invalid, setInvalid] = useState(false);
  const [taxClass, setTaxClass] = useState<string | null>();
  const choices = useTaxChoices(storeId, revision, action === "taxClass");
  const taxValid = taxAssignmentValid(taxClass, choices);
  const [categories, reloadCategories] = useRequest(`bulk-categories:${storeId}:${action === "categories"}:${revision}`, async signal => {
    if (action !== "categories") return { active: [], archived: [] };
    const [active, archivedCategories] = await Promise.all([api.categories(storeId, signal), api.categories(storeId, signal, true)]);
    return { active, archived: archivedCategories };
  });
  const categoriesReady = categories.status === "ready" && !categories.refreshing && categories.refreshError === null;
  const allCategories = categories.status === "ready" ? [...new Map([...categories.data.archived, ...categories.data.active].map(category => [category.id, category])).values()] : [];
  const missingIds = Object.keys(categoryActions).filter(id => !allCategories.some(category => category.id === id));

  function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled || hidden) return;
    const numeric = action === "setPrice" ? bulkPrice(value, currency) : action === "changePrice" ? bulkPercent(value) : 0;
    const add = action === "categories" ? Object.keys(categoryActions).filter(id => categoryActions[id] === "add") : [];
    const remove = action === "categories" ? Object.keys(categoryActions).filter(id => categoryActions[id] === "remove") : [];
    const invalidCategories = action === "categories" && (!categoriesReady || !add.length && !remove.length || missingIds.length > 0
      || categories.status === "ready" && add.some(id => !categories.data.active.some(category => category.id === id)));
    if (numeric === null || invalidCategories || action === "taxClass" && !taxValid) {
      setInvalid(true); document.getElementById(action === "taxClass" ? "bulk-taxClass" : action === "categories" ? "bulk-categories" : "bulk-value")?.focus(); return;
    }
    setInvalid(false);
    onReview({ action, change: bulkChange(action, numeric, add, remove, taxClass ?? null),
      taxClassName: choices.state.status === "ready" ? choices.state.data.items.find(item => item.code === taxClass)?.name : undefined, categoryNames: {
      add: add.map(id => allCategories.find(category => category.id === id)!.name), remove: remove.map(id => allCategories.find(category => category.id === id)!.name),
    } });
  }
  return <form className="bulk-listing-form" aria-label={t("form")} hidden={hidden} noValidate onSubmit={review}>
    <fieldset className="bulk-fields" disabled={disabled}>
      <legend>{t("chooseAction")}</legend>
      <div className="listing-field"><label htmlFor="bulk-action">{t("action")}</label><select id="bulk-action" value={action} onChange={event => {
        setAction(event.target.value as BulkListingAction); setInvalid(false); setValue("");
      }}>{(archived ? ["restore"] as const : activeActions).map(option => <option key={option} value={option}>{t(`actions.${option}`)}</option>)}</select></div>
      {(action === "setPrice" || action === "changePrice") && <Field id="bulk-value" name={action === "setPrice" ? "set" : "byPercent"} label={t(action === "setPrice" ? "setPrice" : "percentage", { currency })}
        inputMode={action === "setPrice" ? "decimal" : "text"} value={value} onChange={event => { setValue(event.target.value); setInvalid(false); }}
        hint={t(action === "setPrice" ? "priceHint" : "percentageHint", { decimals: bulkPriceDecimals(currency), currency })}
        error={invalid ? t(action === "setPrice" ? "invalidPrice" : "invalidPercentage", { decimals: bulkPriceDecimals(currency) }) : undefined} />}
      {action === "taxClass" && <TaxClassField id="bulk-taxClass" choices={choices} value={taxClass} disabled={disabled}
        invalid={invalid} onChange={value => { setTaxClass(value); setInvalid(false); }} />}
      {action === "categories" && <>
        <p className="hint">{t("categoriesHint")}</p><ListingRead state={categories} reload={reloadCategories} label={t("loadingCategories")} />
        {categories.status === "ready" && <fieldset className="bulk-fields" id="bulk-categories" tabIndex={-1} disabled={!categoriesReady}>
          <legend>{t("categoryChanges")}</legend>
          {invalid && <InlineMessage tone="error">{t("invalidCategories")}</InlineMessage>}
          {missingIds.map(id => <div key={id}><p>{t("unavailableCategory", { id })}</p><Button type="button" variant="secondary" onClick={() => setCategoryActions(current => {
            const next = { ...current }; delete next[id]; return next;
          })}>{t("removeUnavailable", { id })}</Button></div>)}
          {allCategories.map(category => {
            const isArchived = !categories.data.active.some(candidate => candidate.id === category.id);
            return <div className="bulk-category-choice" key={category.id}><label htmlFor={`bulk-category-${category.id}`}>{categoryPath(allCategories, category.id).map(part => part.name).join(" › ")}{isArchived && <span> · {t("archivedCategory")}</span>}</label>
              <select id={`bulk-category-${category.id}`} value={categoryActions[category.id] ?? ""} onChange={event => {
                const next = { ...categoryActions }; if (event.target.value) next[category.id] = event.target.value as "add" | "remove"; else delete next[category.id]; setCategoryActions(next); setInvalid(false);
              }}><option value="">{t("unchanged")}</option>{!isArchived && <option value="add">{t("addCategory")}</option>}<option value="remove">{t("removeCategory")}</option></select></div>;
          })}
          {!allCategories.length && <p>{t("noCategories")}</p>}
        </fieldset>}
      </>}
      <p className="hint">{t(`hints.${action}`)}</p>
      <Button type="submit" disabled={action === "categories" && !categoriesReady || action === "taxClass" && !taxValid}>{t("review")}</Button>
    </fieldset>
  </form>;
}
