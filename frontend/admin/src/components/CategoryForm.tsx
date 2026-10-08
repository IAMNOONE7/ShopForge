import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { Category, CategoryInput } from "../api";
import { generatedCode } from "./attributeValidation";
import { canParentCategory, categoryPath } from "./categoryTree";
import { categoryDraft, categoryInput, validateCategory, type CategoryDraft, type CategoryIssue } from "./categoryValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export function CategoryForm({ categories, category, disabled, pending, onSave, onCancel }: {
  categories: Category[]; category?: Category; disabled: boolean; pending: boolean;
  onSave: (input: CategoryInput) => Promise<void>; onCancel?: () => void;
}) {
  const { t } = useTranslation("categories");
  const [draft, setDraft] = useState(() => categoryDraft(category));
  const [issues, setIssues] = useState<CategoryIssue[]>([]);
  function update(field: keyof CategoryDraft, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) => current.filter((issue) => issue.field !== field));
  }
  function error(field: keyof CategoryDraft) {
    const issue = issues.find((candidate) => candidate.field === field);
    return issue ? t(`validation.${issue.key}`) : undefined;
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled) return;
    const next = validateCategory(draft, categories, category?.id);
    setIssues(next);
    if (next.length) {
      document.getElementById(`category-${next[0].field}`)?.focus();
      return;
    }
    await onSave(categoryInput(draft));
  }
  const unavailableParent = draft.parentId && !categories.some((candidate) => candidate.id === draft.parentId);
  return <form className="category-form" noValidate onSubmit={submit}>
    <h2 tabIndex={-1}>{t(category ? "editDetails" : "newCategory")}</h2>
    {issues.length > 0 && <div className="validation-summary" role="alert">{t("checkFields")}</div>}
    <fieldset className="category-fields" disabled={disabled}>
      <Field id="category-name" name="name" label={t("name")} hint={t("nameHint")} value={draft.name}
        error={error("name")} onChange={(event) => update("name", event.target.value)} />
      <Field id="category-slug" name="slug" label={t("slug")} hint={t(category ? "slugEditHint" : "slugCreateHint")}
        value={draft.slug} error={error("slug")} onChange={(event) => update("slug", event.target.value)} />
      {!category && !draft.slug && generatedCode(draft.name) && <p className="hint">{t("slugPreview", { slug: generatedCode(draft.name) })}</p>}
      <div className="category-field-row">
        <Field id="category-sortOrder" name="sortOrder" label={t("sortOrder")} hint={t("sortHint")} inputMode="numeric"
          value={draft.sortOrder} error={error("sortOrder")} onChange={(event) => update("sortOrder", event.target.value)} />
        <div className="category-parent-field">
          <label htmlFor="category-parentId">{t("parent")}</label>
          <select id="category-parentId" name="parentId" value={draft.parentId} aria-invalid={!!error("parentId") || undefined}
            aria-describedby={error("parentId") ? "category-parent-hint category-parent-error" : "category-parent-hint"}
            onChange={(event) => update("parentId", event.target.value)}>
            <option value="">{t("root")}</option>
            {unavailableParent && <option value={draft.parentId}>{t("unavailableParent", { id: draft.parentId })}</option>}
            {categories.map((candidate) => {
              const allowed = canParentCategory(categories, candidate.id, category?.id);
              const path = categoryPath(categories, candidate.id).map((part) => part.name).join(" › ");
              return <option key={candidate.id} value={candidate.id} disabled={!allowed}>
                {allowed ? path : t("parentNotAllowed", { path })}
              </option>;
            })}
          </select>
          <span id="category-parent-hint" className="hint">{t("parentHint")}</span>
          {error("parentId") && <span id="category-parent-error" className="field-error">{error("parentId")}</span>}
        </div>
      </div>
    </fieldset>
    <div className="cluster">
      <Button type="submit" disabled={disabled} busy={pending} busyLabel={t("saving")}>{t(category ? "saveDetails" : "create")}</Button>
      {onCancel && <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button>}
    </div>
  </form>;
}
