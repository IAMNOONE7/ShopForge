import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AttributeDefinition, AttributeValues } from "../api";
import { fieldName } from "./attributeValues";
import { attributeDraft, attributeInput, validAttribute, type AttributeDraft } from "./listingAttributes";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export function ListingAttributeForm({ attributes, values, disabled, pending, onSave, onCancel }: {
  attributes: AttributeDefinition[]; values: AttributeValues; disabled: boolean; pending: boolean;
  onSave: (values: AttributeValues) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation(["listings", "common", "attributes"]);
  const [draft, setDraft] = useState(() => attributeDraft(attributes, values));
  const [blocked, setBlocked] = useState(() => attributes.filter((attribute) => attribute.type === "integer" && typeof values[attribute.code] === "number" && !Number.isSafeInteger(values[attribute.code])).map((attribute) => attribute.code));
  const [unknown, setUnknown] = useState(() => Object.keys(values).filter((code) => !attributes.some((attribute) => attribute.code === code)));
  const [issues, setIssues] = useState<string[]>([]);
  function update(code: string, value: AttributeDraft[string]) {
    setDraft((current) => ({ ...current, [code]: value })); setIssues((current) => current.filter((key) => key !== code)); setBlocked((current) => current.filter((key) => key !== code));
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled) return;
    const invalid = attributes.filter((attribute) => blocked.includes(attribute.code) || !validAttribute(attribute, draft[attribute.code])).map((attribute) => attribute.code);
    setIssues(invalid);
    if (invalid.length || unknown.length) { document.getElementById(invalid.length ? fieldName(invalid[0]) : "listing-values-heading")?.focus(); return; }
    await onSave(attributeInput(attributes, draft));
  }
  return <form className="listing-form" noValidate onSubmit={submit}><h2 id="listing-values-heading" tabIndex={-1}>{t("listings:editValues")}</h2><p className="hint">{t("listings:valuesHint")}</p>
    {issues.length > 0 && <div className="validation-summary" role="alert">{t("listings:checkFields")}</div>}
    <fieldset disabled={disabled} className="listing-fields"><legend>{t("listings:values")}</legend>
      {unknown.map((code) => <div key={code}><p>{t("listings:unknownValue", { code })}</p><Button type="button" variant="secondary" onClick={() => setUnknown((current) => current.filter((key) => key !== code))}>{t("listings:clearValue", { name: code })}</Button></div>)}
      {attributes.map((attribute) => {
        const id = fieldName(attribute.code); const label = `${attribute.name}${attribute.unit ? ` (${attribute.unit})` : ""}`;
        const value = draft[attribute.code]; const error = issues.includes(attribute.code) ? t("listings:validation.valueInvalid") : undefined;
        const missing = (Array.isArray(value) ? value : attribute.type === "select" && value ? [value] : []).filter((code) => !attribute.options.some((option) => option.code === code));
        return <div key={attribute.id} className="listing-field">
          {attribute.type === "multiSelect" ? <fieldset id={id} tabIndex={-1} className="listing-fields" aria-invalid={!!error || undefined} aria-describedby={error ? `${id}-error` : undefined}>
            <legend>{label}</legend>{[...attribute.options.map((option) => ({ code: option.code, name: option.name })), ...missing.map((code) => ({ code, name: t("listings:unknownOption", { code }) }))].map((option) => <label className="listing-check" key={option.code}>
              <input type="checkbox" name={id} value={option.code} checked={Array.isArray(value) && value.includes(option.code)} onChange={(event) => update(attribute.code, event.target.checked ? [...(Array.isArray(value) ? value : []), option.code] : (Array.isArray(value) ? value : []).filter((code) => code !== option.code))} />{option.name}</label>)}
          </fieldset> : attribute.type === "select" || attribute.type === "boolean" ? <>
            <label htmlFor={id}>{label}</label><select id={id} name={id} value={String(value)} aria-invalid={!!error || undefined} aria-describedby={error ? `${id}-error` : undefined} onChange={(event) => update(attribute.code, event.target.value)}>
              <option value="">{t("listings:notSet")}</option>
              {attribute.type === "boolean" ? <><option value="true">{t("common:yes")}</option><option value="false">{t("common:no")}</option></> : <>
                {missing.map((code) => <option key={code} value={code}>{t("listings:unknownOption", { code })}</option>)}{attribute.options.map((option) => <option key={option.code} value={option.code}>{option.name}</option>)}
              </>}
            </select></> : <Field id={id} name={id} label={label} type={attribute.type === "date" ? "date" : "text"} value={blocked.includes(attribute.code) ? "" : String(value)}
              inputMode={attribute.type === "decimal" ? "decimal" : undefined} hint={t(`listings:valueHints.${attribute.type}`)} error={error} onChange={(event) => update(attribute.code, event.target.value)} />}
          {error && ["multiSelect", "select", "boolean"].includes(attribute.type) && <span className="field-error" id={`${id}-error`}>{error}</span>}
          {blocked.includes(attribute.code) && <div><p className="field-error">{t("listings:unsafeInteger")}</p><Button type="button" variant="secondary" onClick={() => update(attribute.code, "")}>{t("listings:clearValue", { name: attribute.name })}</Button></div>}
        </div>;
      })}
      {!attributes.length && <p>{t("listings:noDefinitions")}</p>}
    </fieldset><div className="cluster"><Button type="submit" disabled={disabled} busy={pending} busyLabel={t("listings:saving")}>{t("listings:saveValues")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("listings:cancel")}</Button></div>
  </form>;
}
