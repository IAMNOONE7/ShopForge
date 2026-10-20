import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { parseTaxPercentage, taxCountry, validTaxCode, validTaxName, type TaxClass, type TaxRate } from "../api/taxClasses";
import { formatNumber } from "../utils/format";
import { generatedCode } from "./attributeValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export type TaxEditor = { kind: "create" }
  | { kind: "rename" | "deleteClass" | "addRate"; item: TaxClass }
  | { kind: "editRate" | "removeRate"; item: TaxClass; rate: TaxRate };
export type TaxChange = { kind: TaxEditor["kind"]; code: string; name: string; country: string; rate: number | null };
type TaxIssue = { field: "name" | "code" | "country" | "rate"; key: "nameInvalid" | "codeInvalid" | "codeDuplicate" | "countryInvalid" | "countryDuplicate" | "rateInvalid" };

export function TaxClassEditor({ editor, items, disabled, pending, onCancel, onSave }: {
  editor: TaxEditor; items: TaxClass[]; disabled: boolean; pending: boolean;
  onCancel: () => void; onSave: (change: TaxChange) => Promise<void>;
}) {
  const { t, i18n } = useTranslation("tax");
  const [name, setName] = useState("item" in editor ? editor.item.name : "");
  const [code, setCode] = useState("");
  const [country, setCountry] = useState("rate" in editor ? editor.rate.country : "");
  const [rate, setRate] = useState("rate" in editor ? String(editor.rate.rate) : "");
  const [submitted, setSubmitted] = useState(false);
  const root = useRef<HTMLFormElement>(null);
  useEffect(() => { root.current?.querySelector<HTMLElement>("h2")?.focus(); }, []);
  const creating = editor.kind === "create", naming = creating || editor.kind === "rename";
  const pricing = editor.kind === "addRate" || editor.kind === "editRate";
  const effectiveCode = creating ? code.trim() || generatedCode(name) : editor.item.code;
  const normalized = taxCountry(country), parsed = parseTaxPercentage(rate);
  const codeIssue = !validTaxCode(effectiveCode) ? "codeInvalid" : items.some(item => item.code === effectiveCode) ? "codeDuplicate" : null;
  const countryIssue = !normalized ? "countryInvalid" : editor.kind === "addRate" && editor.item.rates.some(rate => rate.country === normalized) ? "countryDuplicate" : null;
  const issues: TaxIssue[] = [];
  if (naming && !validTaxName(name)) issues.push({ field: "name", key: "nameInvalid" });
  if (creating && codeIssue) issues.push({ field: "code", key: codeIssue });
  if (pricing && countryIssue) issues.push({ field: "country", key: countryIssue });
  if (pricing && parsed === null) issues.push({ field: "rate", key: "rateInvalid" });
  function errorFor(field: TaxIssue["field"]) {
    const issue = submitted ? issues.find(issue => issue.field === field) : undefined;
    return issue ? t(`validation.${issue.key}`) : undefined;
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); if (disabled || pending) return;
    setSubmitted(true);
    if (issues.length) { document.getElementById(`tax-${issues[0].field}`)?.focus(); return; }
    await onSave({ kind: editor.kind, code: effectiveCode, name: name.trim(), country: normalized ?? "", rate: parsed });
  }
  return <form ref={root} className="tax-editor" aria-labelledby="tax-editor-title" noValidate onSubmit={submit}
    onKeyDown={event => { if (event.key === "Escape" && !pending) { event.preventDefault(); onCancel(); } }}>
    <h2 id="tax-editor-title" tabIndex={-1}>{t(`editors.${editor.kind}`)}</h2>
    {"item" in editor && <p>{editor.item.name} · <code>{editor.item.code}</code></p>}
    {naming && <Field id="tax-name" name="name" label={t("name")} value={name} maxLength={100} required disabled={disabled || pending}
      error={errorFor("name")} onChange={event => setName(event.target.value)} />}
    {creating && <><Field id="tax-code" name="code" label={t("code")} hint={t("codeHint")} value={code} maxLength={50} disabled={disabled || pending}
      error={errorFor("code")} onChange={event => setCode(event.target.value)} />
      <p className="hint">{t("effectiveCode", { code: effectiveCode || "—" })}</p></>}
    {editor.kind === "rename" && <p className="hint">{t("immutableCode")}</p>}
    {pricing && <>
      {editor.kind === "addRate" ? <Field id="tax-country" name="country" label={t("country")} hint={t("countryHint")} value={country} maxLength={8} required autoCapitalize="characters" disabled={disabled || pending}
        error={errorFor("country")} onChange={event => setCountry(event.target.value)} /> : <p>{t("country")}: <strong>{country}</strong></p>}
      <Field id="tax-rate" name="rate" label={t("rate")} hint={t("rateHint")} value={rate} inputMode="decimal" required disabled={disabled || pending}
        error={errorFor("rate")} onChange={event => setRate(event.target.value)} />
    </>}
    {editor.kind === "deleteClass" && <p>{t("deleteHint", { count: editor.item.rates.length })}</p>}
    {editor.kind === "removeRate" && <><p>{editor.rate.country} · {formatNumber(editor.rate.rate, i18n.resolvedLanguage)} %</p><p>{t("removeHint")}</p></>}
    <div className="cluster">
      <Button type="submit" variant={editor.kind === "deleteClass" || editor.kind === "removeRate" ? "danger" : "primary"} disabled={disabled} busy={pending} busyLabel={t("working")}>{t(editor.kind === "deleteClass" ? "deleteClass" : editor.kind === "removeRate" ? "removeRate" : "save")}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button>
    </div>
  </form>;
}
