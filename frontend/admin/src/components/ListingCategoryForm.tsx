import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { Category, StoreProduct } from "../api";
import { categoryPath } from "./categoryTree";
import { Button } from "./ui/Button";

export function ListingCategoryForm({ item, categories, disabled, pending, onSave, onCancel }: {
  item: StoreProduct; categories: Category[]; disabled: boolean; pending: boolean; onSave: (ids: string[]) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation("listings");
  const [ids, setIds] = useState(() => [...item.categoryIds]);
  const [invalid, setInvalid] = useState(false);
  const unknown = ids.filter((id) => !categories.some((category) => category.id === id));
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled) return;
    if (unknown.length) { setInvalid(true); document.getElementById("categoryIds")?.focus(); return; }
    await onSave(ids);
  }
  return <form className="listing-form" noValidate onSubmit={submit}><h2 tabIndex={-1}>{t("editCategories")}</h2><p className="hint">{t("categoriesHint")}</p>
    {invalid && <div role="alert" className="validation-summary">{t("validation.categoriesInvalid")}</div>}
    <fieldset id="categoryIds" tabIndex={-1} disabled={disabled} className="listing-fields"><legend>{t("categories")}</legend>
      {unknown.map((id) => <div key={id}><p>{t("unknownCategory", { id })}</p><Button type="button" variant="secondary" onClick={() => { setIds((current) => current.filter((value) => value !== id)); setInvalid(false); }}>{t("removeCategory", { id })}</Button></div>)}
      {categories.map((category) => <label className="listing-check" key={category.id}><input type="checkbox" name="categoryIds" value={category.id} checked={ids.includes(category.id)} onChange={(event) => setIds((current) => event.target.checked ? [...current, category.id] : current.filter((id) => id !== category.id))} />
        <span>{categoryPath(categories, category.id).map((part) => part.name).join(" › ")}</span></label>)}
      {!categories.length && <p>{t("noCategories")}</p>}
    </fieldset><p className="hint">{t("clearCategoriesHint")}</p><div className="cluster">
      <Button type="submit" disabled={disabled} busy={pending} busyLabel={t("saving")}>{t("saveCategories")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button>
    </div></form>;
}
