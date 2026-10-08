import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AttributeDefinition, Category } from "../api";
import { Button } from "./ui/Button";

export function CategoryAttributeForm({ category, attributes, disabled, pending, onSave, onCancel }: {
  category: Category; attributes: AttributeDefinition[]; disabled: boolean; pending: boolean;
  onSave: (ids: string[]) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation(["categories", "attributes"]);
  const [ids, setIds] = useState(() => [...category.attributeIds]);
  const [invalid, setInvalid] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  function label(id: string) {
    return attributes.find((attribute) => attribute.id === id)?.name ?? t("categories:unknownAttribute", { id });
  }
  function move(index: number, direction: number) {
    setIds((current) => {
      const next = [...current];
      [next[index], next[index + direction]] = [next[index + direction], next[index]];
      return next;
    });
    window.requestAnimationFrame(() => document.getElementById(`category-attribute-${ids[index]}`)?.focus());
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled) return;
    if (ids.some((id) => !attributes.some((attribute) => attribute.id === id)) || new Set(ids).size !== ids.length) {
      setInvalid(true); heading.current?.focus(); return;
    }
    await onSave(ids);
  }
  return <form className="category-attribute-form" noValidate onSubmit={submit}>
    <h2 ref={heading} tabIndex={-1}>{t("categories:editAttributes")}</h2>
    <p className="hint">{t("categories:attributesHint")}</p>
    {invalid && <div className="validation-summary" role="alert">{t("categories:attributesInvalid")}</div>}
    <fieldset id="attributeIds" tabIndex={-1} disabled={disabled} className="category-fields">
      <legend>{t("categories:assignedAttributes")}</legend>
      {ids.length ? <ol className="category-attribute-order">{ids.map((id, index) => <li key={id}>
        <strong id={`category-attribute-${id}`} tabIndex={-1}>{label(id)}</strong>
        <div className="cluster">
          <Button type="button" variant="secondary" disabled={index === 0} onClick={() => move(index, -1)}>{t("categories:moveUp", { name: label(id) })}</Button>
          <Button type="button" variant="secondary" disabled={index === ids.length - 1} onClick={() => move(index, 1)}>{t("categories:moveDown", { name: label(id) })}</Button>
          <Button type="button" variant="quiet" onClick={() => {
            setIds((current) => current.filter((value) => value !== id)); setInvalid(false);
            window.requestAnimationFrame(() => heading.current?.focus());
          }}>{t("categories:removeAttribute", { name: label(id) })}</Button>
        </div>
      </li>)}</ol> : <p>{t("categories:noAssignedAttributes")}</p>}
      <div className="category-attribute-choices">
        <h3>{t("categories:availableAttributes")}</h3>
        {attributes.filter((attribute) => !ids.includes(attribute.id)).map((attribute) => <div key={attribute.id} className="category-attribute-choice">
          <div><strong>{attribute.name}</strong><p className="hint"><code>{attribute.code}</code> · {t(`attributes:types.${attribute.type}`)} · {t(attribute.isFilterable ? "categories:filterable" : "categories:notFilterable")}</p></div>
          <Button type="button" variant="secondary" onClick={() => {
            setIds((current) => [...current, attribute.id]); setInvalid(false);
            window.requestAnimationFrame(() => document.getElementById(`category-attribute-${attribute.id}`)?.focus());
          }}>{t("categories:addAttribute", { name: attribute.name })}</Button>
        </div>)}
        {attributes.length === 0 && <p>{t("categories:noDefinitions")}</p>}
      </div>
    </fieldset>
    <p className="hint">{t("categories:unassignHint")}</p>
    <div className="cluster">
      <Button type="submit" disabled={disabled} busy={pending} busyLabel={t("categories:saving")}>{t("categories:saveAttributes")}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("categories:cancel")}</Button>
    </div>
  </form>;
}
