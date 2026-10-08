import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { Product, ProductOptionsInput, ProductVariant, VariantInput } from "../api";
import { conditions } from "./productValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import {
  optionsDraft, validateOptions, validateVariant, variantDraft, variantInput,
  type VariantIssue,
} from "./variantValidation";

function ValidationSummary({ issues, labels }: { issues: VariantIssue[]; labels: Record<string, string> }) {
  const { t } = useTranslation("products");
  if (!issues.length) return null;
  return (
    <div className="validation-summary" id="variant-validation" role="alert" tabIndex={-1}>
      <strong>{t("validationTitle")}</strong>
      <ul>{issues.map((issue) => (
        <li key={issue.field}>
          <button type="button" className="link-button" onClick={() => {
            const field = document.getElementsByName(issue.field)[0];
            if (field instanceof HTMLElement) field.focus();
          }}>{labels[issue.field]}: {t(`validation.${issue.key}`)}</button>
        </li>
      ))}</ul>
    </div>
  );
}

function focusValidation() {
  window.requestAnimationFrame(() => document.getElementById("variant-validation")?.focus());
}

export function VariantForm({ product, variant, disabled, pending, onSave, onCancel }: {
  product: Product; variant?: ProductVariant; disabled: boolean; pending: boolean;
  onSave: (input: VariantInput) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation("products");
  const [draft, setDraft] = useState(() => ({ ...variantDraft(variant),
    optionValues: product.optionNames.map((_, index) => variant?.optionValues[index] ?? ""),
  }));
  const [issues, setIssues] = useState<VariantIssue[]>([]);
  const labels = { sku: t("sku"), ean: t("ean"), weightGrams: t("weight"), partNumber: t("partNumber"),
    ...Object.fromEntries(product.optionNames.map((name, index) => [`optionValues.${index}`, name])) };
  function update(field: "sku" | "ean" | "weightGrams" | "partNumber" | "condition", value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) => current.filter((issue) => issue.field !== field));
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled) return;
    const next = validateVariant(draft, product.optionNames);
    setIssues(next);
    if (next.length) { focusValidation(); return; }
    await onSave(variantInput(draft));
  }
  return (
    <form className="variant-editor" noValidate onSubmit={submit}>
      <h3 tabIndex={-1}>{variant ? t("editVariantTitle", { sku: variant.sku }) : t("addVariant")}</h3>
      <ValidationSummary issues={issues} labels={labels} />
      <fieldset disabled={disabled} className="variant-fields">
        <Field name="sku" label={t("sku")} hint={t("variantSkuHint")} value={draft.sku}
          error={issues.find((issue) => issue.field === "sku") && t("validation.skuInvalid")}
          onChange={(event) => update("sku", event.target.value)} />
        <div className="physical-fields-row">
          <Field name="ean" label={t("eanOptional")} hint={t("eanHint")} inputMode="numeric"
            value={draft.ean} error={issues.find((issue) => issue.field === "ean") && t("validation.eanInvalid")}
            onChange={(event) => update("ean", event.target.value)} />
          <Field name="weightGrams" label={t("weightGrams")} hint={t("weightHint")} inputMode="numeric"
            value={draft.weightGrams} error={issues.find((issue) => issue.field === "weightGrams") && t("validation.weightInvalid")}
            onChange={(event) => update("weightGrams", event.target.value)} />
        </div>
        <div className="physical-fields-row">
          <Field name="partNumber" label={t("partNumber")} hint={t("partNumberHint")}
            value={draft.partNumber} error={issues.find((issue) => issue.field === "partNumber") && t("validation.partNumberInvalid")}
            onChange={(event) => update("partNumber", event.target.value)} />
          <label>{t("condition")}<select name="condition" value={draft.condition}
            onChange={(event) => update("condition", event.target.value)}>
            {conditions.map((condition) => <option key={condition} value={condition}>
              {condition ? t(`condition${condition}`) : t("conditionUnstated")}
            </option>)}
          </select></label>
        </div>
        {product.optionNames.length > 0 && (
          <div id="optionValues" tabIndex={-1} className="variant-option-fields">
            {product.optionNames.map((name, index) => <Field key={index} name={`optionValues.${index}`}
              label={name} value={draft.optionValues[index]}
              error={issues.find((issue) => issue.field === `optionValues.${index}`) && t("validation.optionValueRequired")}
              onChange={(event) => {
                setDraft((current) => ({ ...current, optionValues: current.optionValues.map((value, position) =>
                  position === index ? event.target.value : value) }));
                setIssues((current) => current.filter((issue) => issue.field !== `optionValues.${index}`));
              }} />)}
          </div>
        )}
      </fieldset>
      <div className="cluster">
        <Button type="submit" disabled={disabled} busy={pending} busyLabel={t("savingProduct")}>
          {variant ? t("saveVariant") : t("createVariant")}
        </Button>
        <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button>
      </div>
    </form>
  );
}

export function ProductOptionsForm({ product, disabled, pending, onSave, onCancel }: {
  product: Product; disabled: boolean; pending: boolean;
  onSave: (input: ProductOptionsInput) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation("products");
  const [draft, setDraft] = useState(() => optionsDraft(product));
  const [issues, setIssues] = useState<VariantIssue[]>([]);
  const labels = Object.fromEntries(draft.names.flatMap((name, index) => [
    [`names.${index}`, t("axisName", { number: index + 1 })],
    ...product.variants.map((variant) => [`values.${variant.id}.${index}`, t("axisValue", {
      sku: variant.sku, name: name || t("axisName", { number: index + 1 }),
    })]),
  ]));
  function change(next: ProductOptionsInput) { setDraft(next); setIssues([]); }
  function move(index: number, direction: number) {
    function reordered(values: string[]) {
      const next = [...values];
      [next[index], next[index + direction]] = [next[index + direction], next[index]];
      return next;
    }
    change({ names: reordered(draft.names), values: Object.fromEntries(
      Object.entries(draft.values).map(([id, values]) => [id, reordered(values)])) });
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled) return;
    const next = validateOptions(draft, product);
    setIssues(next);
    if (next.length) { focusValidation(); return; }
    await onSave({ names: draft.names.map((name) => name.trim()), values: Object.fromEntries(
      Object.entries(draft.values).map(([id, values]) => [id, values.map((value) => value.trim())])) });
  }
  return (
    <form className="variant-editor" noValidate onSubmit={submit}>
      <h3 tabIndex={-1}>{t("editOptions")}</h3><p className="hint">{t("optionsEditHint")}</p>
      <ValidationSummary issues={issues} labels={labels} />
      <fieldset disabled={disabled} className="variant-fields" id="names" tabIndex={-1}>
        {draft.names.map((name, index) => <div className="variant-axis" key={index}>
          <div className="variant-axis-heading">
            <Field name={`names.${index}`} label={t("axisName", { number: index + 1 })} value={name}
              error={issues.find((issue) => issue.field === `names.${index}`) && t(`validation.${issues.find((issue) => issue.field === `names.${index}`)!.key}`)}
              onChange={(event) => change({ ...draft, names: draft.names.map((value, position) => position === index ? event.target.value : value) })} />
            <div className="cluster">
              <Button type="button" variant="secondary" disabled={index === 0} onClick={() => move(index, -1)}>{t("moveAxisUp", { number: index + 1 })}</Button>
              <Button type="button" variant="secondary" disabled={index === draft.names.length - 1} onClick={() => move(index, 1)}>{t("moveAxisDown", { number: index + 1 })}</Button>
              <Button type="button" variant="quiet" onClick={() => change({
                names: draft.names.filter((_, position) => position !== index),
                values: Object.fromEntries(Object.entries(draft.values).map(([id, values]) => [id, values.filter((_, position) => position !== index)])),
              })}>{t("removeAxis", { number: index + 1 })}</Button>
            </div>
          </div>
          {product.variants.map((variant) => <Field key={variant.id} name={`values.${variant.id}.${index}`}
            label={t("axisValue", { sku: variant.sku, name: name || t("axisName", { number: index + 1 }) })}
            value={draft.values[variant.id][index] ?? ""}
            error={issues.find((issue) => issue.field === `values.${variant.id}.${index}`) && t("validation.optionValueRequired")}
            onChange={(event) => change({ ...draft, values: { ...draft.values,
              [variant.id]: draft.values[variant.id].map((value, position) => position === index ? event.target.value : value),
            } })} />)}
        </div>)}
        {!draft.names.length && <p>{t("noOptions")}</p>}
        <Button type="button" variant="secondary" disabled={draft.names.length >= 3} onClick={() => change({
          names: [...draft.names, ""], values: Object.fromEntries(Object.entries(draft.values).map(([id, values]) => [id, [...values, ""]])),
        })}>{t("addAxis")}</Button>
      </fieldset>
      <p className="hint">{t("removeAxesHint")}</p>
      <div className="cluster">
        <Button type="submit" disabled={disabled} busy={pending} busyLabel={t("savingProduct")}>{t("saveOptions")}</Button>
        <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button>
      </div>
    </form>
  );
}
