import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type AttributeDefinition, type AttributeType } from "../api";
import { useAction } from "../useAction";
import {
  attributeTypes,
  generatedCode,
  optionNames,
  validateAttribute,
  type AttributeDraft,
  type AttributeIssue,
} from "./attributeValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

function validationFor(issues: AttributeIssue[], field: AttributeIssue["field"]) {
  return issues.find((issue) => issue.field === field)?.key;
}

function Fields({
  draft,
  issues,
  creating,
  update,
}: {
  draft: AttributeDraft;
  issues: AttributeIssue[];
  creating: boolean;
  update: <K extends keyof AttributeDraft>(field: K, value: AttributeDraft[K]) => void;
}) {
  const { t } = useTranslation("attributes");
  const error = (field: AttributeIssue["field"]) => {
    const key = validationFor(issues, field);
    return key ? t(`validation.${key}`) : undefined;
  };

  return (
    <>
      <Field
        id="attribute-name"
        name="name"
        label={t("name")}
        hint={t("nameHint")}
        maxLength={200}
        autoFocus={creating}
        value={draft.name}
        error={error("name")}
        onChange={(event) => update("name", event.target.value)}
      />
      {creating ? (
        <Field
          id="attribute-code"
          name="code"
          label={t("codeOptional")}
          hint={t("codeAutoHint", { code: generatedCode(draft.name.trim()) || "—" })}
          maxLength={120}
          value={draft.code}
          error={error("code")}
          onChange={(event) => update("code", event.target.value)}
        />
      ) : (
        <Field
          id="attribute-code"
          name="code"
          label={t("code")}
          value={draft.code}
          readOnly
          hint={t("codeReadOnly")}
        />
      )}
      {creating ? (
        <div className="attribute-select-field">
          <label htmlFor="attribute-type">{t("type")}</label>
          <select
            id="attribute-type"
            name="type"
            value={draft.type}
            onChange={(event) => {
              const type = event.target.value as AttributeType;
              update("type", type);
              if (type === "text") update("isFilterable", false);
              if (type !== "select" && type !== "multiSelect") update("options", "");
            }}
          >
            {attributeTypes.map((type) => (
              <option key={type} value={type}>{t(`types.${type}`)}</option>
            ))}
          </select>
          <span className="hint">{t("typeHint")}</span>
        </div>
      ) : (
        <Field
          id="attribute-type"
          name="type"
          label={t("type")}
          value={t(`types.${draft.type}`)}
          readOnly
          hint={t("typeReadOnly")}
        />
      )}
      <div className="attribute-fields-row">
        <Field
          id="attribute-unit"
          name="unit"
          label={t("unitOptional")}
          hint={t("unitHint")}
          maxLength={20}
          value={draft.unit}
          error={error("unit")}
          onChange={(event) => update("unit", event.target.value)}
        />
        <Field
          id="attribute-sort"
          name="sortOrder"
          label={t("sortOrder")}
          hint={t("sortOrderHint")}
          inputMode="numeric"
          value={draft.sortOrder}
          error={error("sortOrder")}
          onChange={(event) => update("sortOrder", event.target.value)}
        />
      </div>
      <fieldset className="attribute-flags">
        <legend>{t("displaySettings")}</legend>
        <label>
          <input
            name="isFilterable"
            type="checkbox"
            checked={draft.isFilterable}
            disabled={draft.type === "text"}
            onChange={(event) => update("isFilterable", event.target.checked)}
          />
          {t("filter")}
        </label>
        {draft.type === "text" && <p className="hint">{t("textNotFilterable")}</p>}
        <label>
          <input
            name="isVisibleOnProductPage"
            type="checkbox"
            checked={draft.isVisibleOnProductPage}
            onChange={(event) => update("isVisibleOnProductPage", event.target.checked)}
          />
          {t("showOnProduct")}
        </label>
      </fieldset>
      {creating && (draft.type === "select" || draft.type === "multiSelect") && (
        <div className="attribute-options-input">
          <label htmlFor="attribute-options">{t("initialOptions")}</label>
          <textarea
            id="attribute-options"
            name="options"
            rows={4}
            value={draft.options}
            aria-invalid={Boolean(error("options")) || undefined}
            aria-describedby={error("options") ? "attribute-options-error attribute-options-hint" : "attribute-options-hint"}
            onChange={(event) => update("options", event.target.value)}
          />
          <span id="attribute-options-hint" className="hint">{t("initialOptionsHint")}</span>
          {error("options") && <span id="attribute-options-error" className="field-error">{error("options")}</span>}
        </div>
      )}
    </>
  );
}

const initial: AttributeDraft = {
  name: "",
  code: "",
  type: "select",
  unit: "",
  isFilterable: true,
  isVisibleOnProductPage: true,
  sortOrder: "0",
  options: "",
};

export function NewAttributeForm({
  storeId,
  onCreated,
}: {
  storeId: string;
  onCreated: (definition: AttributeDefinition) => void;
}) {
  const { t } = useTranslation("attributes");
  const [draft, setDraft] = useState<AttributeDraft>(initial);
  const [issues, setIssues] = useState<AttributeIssue[]>([]);
  const [error, run, pending] = useAction(() => undefined);
  function update<K extends keyof AttributeDraft>(field: K, value: AttributeDraft[K]) {
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) => current.filter((issue) => issue.field !== field));
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextIssues = validateAttribute(draft, true);
    setIssues(nextIssues);
    if (nextIssues.length) {
      document.getElementById(`attribute-${nextIssues[0].field === "sortOrder" ? "sort" : nextIssues[0].field}`)?.focus();
      return;
    }
    await run(async () => {
      const definition = await api.createAttribute(storeId, {
        code: draft.code.trim() || null,
        name: draft.name.trim(),
        type: draft.type,
        unit: draft.unit.trim() || null,
        isFilterable: draft.type !== "text" && draft.isFilterable,
        isVisibleOnProductPage: draft.isVisibleOnProductPage,
        sortOrder: Number(draft.sortOrder),
        options: optionNames(draft.options),
      });
      onCreated(definition);
    });
  }
  return (
    <form className="physical-panel attribute-form" noValidate onSubmit={submit}>
      <h2>{t("createTitle")}</h2>
      <p className="hint">{t("contentHint")}</p>
      {issues.length > 0 && <InlineMessage tone="error">{t("checkFields")}</InlineMessage>}
      <Fields draft={draft} issues={issues} creating update={update} />
      <Button type="submit" busy={pending} busyLabel={t("creating")}>{t("create")}</Button>
      {error !== null && <RequestError error={error} operation="write" />}
    </form>
  );
}

export function EditAttributeForm({
  storeId,
  definition,
  reload,
}: {
  storeId: string;
  definition: AttributeDefinition;
  reload: () => void;
}) {
  const { t } = useTranslation("attributes");
  const [draft, setDraft] = useState<AttributeDraft>({
    name: definition.name,
    code: definition.code,
    type: definition.type,
    unit: definition.unit ?? "",
    isFilterable: definition.isFilterable,
    isVisibleOnProductPage: definition.isVisibleOnProductPage,
    sortOrder: String(definition.sortOrder),
    options: "",
  });
  const [issues, setIssues] = useState<AttributeIssue[]>([]);
  const [saved, setSaved] = useState(false);
  const [error, run, pending] = useAction(reload);
  function update<K extends keyof AttributeDraft>(field: K, value: AttributeDraft[K]) {
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) => current.filter((issue) => issue.field !== field));
    setSaved(false);
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextIssues = validateAttribute(draft, false);
    setIssues(nextIssues);
    setSaved(false);
    if (nextIssues.length) {
      document.getElementById(`attribute-${nextIssues[0].field === "sortOrder" ? "sort" : nextIssues[0].field}`)?.focus();
      return;
    }
    await run(async () => {
      const updated = await api.updateAttribute(storeId, definition.id, {
        name: draft.name.trim(),
        unit: draft.unit.trim() || null,
        isFilterable: definition.type !== "text" && draft.isFilterable,
        isVisibleOnProductPage: draft.isVisibleOnProductPage,
        sortOrder: Number(draft.sortOrder),
      });
      setDraft({
        name: updated.name,
        code: updated.code,
        type: updated.type,
        unit: updated.unit ?? "",
        isFilterable: updated.isFilterable,
        isVisibleOnProductPage: updated.isVisibleOnProductPage,
        sortOrder: String(updated.sortOrder),
        options: "",
      });
      setSaved(true);
    });
  }
  return (
    <form className="physical-panel attribute-form" noValidate onSubmit={submit}>
      <h2>{t("editTitle")}</h2>
      <p className="hint">{t("editHint")}</p>
      {issues.length > 0 && <InlineMessage tone="error">{t("checkFields")}</InlineMessage>}
      <Fields draft={draft} issues={issues} creating={false} update={update} />
      <Button type="submit" busy={pending} busyLabel={t("saving")}>{t("save")}</Button>
      {saved && <InlineMessage tone="success">{t("saved")}</InlineMessage>}
      {error !== null && <RequestError error={error} operation="write" />}
    </form>
  );
}
