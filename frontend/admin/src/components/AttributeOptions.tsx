import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AttributeDefinition } from "../api";
import type { AttributeCommand } from "./attributeCommands";
import { validateOption, type AttributeIssueKey } from "./attributeValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

type Option = AttributeDefinition["options"][number];
export type AttributeEditor = { kind: "update" | "add" | "reorder" | "rename" | "removeOption" | "removeDefinition"; baseline: AttributeDefinition; option?: Option };

export function AttributeOptions({ definition, allowed, disabled, editing, begin }: {
  definition: AttributeDefinition; allowed: boolean; disabled: boolean; editing: boolean;
  begin: (kind: AttributeEditor["kind"], button: HTMLButtonElement, option?: Option) => void;
}) {
  const { t } = useTranslation("attributes");
  const hasOptions = ["select", "multiSelect"].includes(definition.type);
  return <section className="physical-panel attribute-options">
    <h2>{t("options")}</h2>
    {!hasOptions ? <p className="hint">{t("noOptionsForType")}</p> : <>
      <p className="hint">{t("optionLifecycleHint")}</p>
      {definition.options.length === 0 ? <p>{t("emptyOptions")}</p> : <ol className="attribute-option-list">
        {definition.options.map(option => <li key={option.id}>
          <div className="attribute-option-identity"><strong>{option.name}</strong><code>{option.code}</code></div>
          {allowed && <div className="cluster">
            <Button type="button" variant="secondary" disabled={disabled || editing} aria-label={t("renameNamed", { name: option.name })} onClick={event => begin("rename", event.currentTarget, option)}>{t("renameOption")}</Button>
            <Button type="button" variant="quiet" disabled={disabled || editing} aria-label={t("removeNamed", { name: option.name })} onClick={event => begin("removeOption", event.currentTarget, option)}>{t("removeOption")}</Button>
          </div>}
        </li>)}
      </ol>}
      {allowed && <div className="cluster">
        <Button type="button" disabled={disabled || editing} onClick={event => begin("add", event.currentTarget)}>{t("addOption")}</Button>
        <Button type="button" variant="secondary" disabled={disabled || editing || definition.options.length < 2} onClick={event => begin("reorder", event.currentTarget)}>{t("reorderOptions")}</Button>
      </div>}
    </>}
  </section>;
}

export function AttributeOptionEditor({ editor, disabled, pending, onSave, onCancel }: {
  editor: AttributeEditor; disabled: boolean; pending: boolean; onSave: (command: AttributeCommand) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation("attributes");
  const [name, setName] = useState(editor.option?.name ?? "");
  const [ids, setIds] = useState(editor.baseline.options.map(option => option.id));
  const [issue, setIssue] = useState<AttributeIssueKey | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled) return;
    if (editor.kind === "reorder") { await onSave({ kind: "reorder", optionIds: ids }); return; }
    const invalid = editor.kind === "add" ? validateOption(name, editor.baseline.options.map(option => option.code))
      : !name.trim() || name.trim().length > 200 ? "nameInvalid" : null;
    setIssue(invalid);
    if (invalid) { document.getElementById("attribute-option-name")?.focus(); return; }
    await onSave(editor.kind === "add" ? { kind: "add", name: name.trim() } : { kind: "rename", name: name.trim(), optionId: editor.option!.id });
  }
  function move(index: number, by: number) {
    const next = [...ids]; [next[index], next[index + by]] = [next[index + by], next[index]]; setIds(next);
  }
  return <form className="attribute-form" noValidate onSubmit={event => void save(event)}>
    <h2 ref={heading} tabIndex={-1}>{t(editor.kind === "reorder" ? "reorderOptions" : editor.kind === "add" ? "addOption" : "renameOption")}</h2>
    {editor.kind === "reorder" ? <>
      <p className="hint">{t("orderHint")}</p>
      <ol className="attribute-option-list">{ids.map((id, index) => {
        const option = editor.baseline.options.find(value => value.id === id)!;
        return <li key={id}><div className="attribute-option-identity"><strong>{option.name}</strong><code>{option.code}</code></div>
          <div className="cluster"><Button type="button" variant="secondary" disabled={disabled || index === 0} aria-label={t("moveUpNamed", { name: option.name })} onClick={() => move(index, -1)}>{t("moveUp")}</Button>
            <Button type="button" variant="secondary" disabled={disabled || index === ids.length - 1} aria-label={t("moveDownNamed", { name: option.name })} onClick={() => move(index, 1)}>{t("moveDown")}</Button></div></li>;
      })}</ol>
    </> : <>
      {editor.option && <p>{t("code")}: <code>{editor.option.code}</code> · {t("codeReadOnly")}</p>}
      <Field id="attribute-option-name" name="name" label={t(editor.kind === "add" ? "newOption" : "optionName")} hint={t(editor.kind === "add" ? "optionHint" : "renameHint")}
        maxLength={200} value={name} disabled={disabled} error={issue ? t(`validation.${issue}`) : undefined} onChange={event => { setName(event.target.value); setIssue(null); }} />
    </>}
    <div className="cluster"><Button type="submit" disabled={disabled}>{t(editor.kind === "reorder" ? "saveOrder" : editor.kind === "add" ? "addOption" : "save")}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancelEdit")}</Button></div>
  </form>;
}
