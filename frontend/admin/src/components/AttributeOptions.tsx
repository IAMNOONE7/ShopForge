import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type AttributeDefinition } from "../api";
import { useAction } from "../useAction";
import { validateOption, type AttributeIssueKey } from "./attributeValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

export function AttributeOptions({ storeId, definition, allowed, reload }: {
  storeId: string;
  definition: AttributeDefinition;
  allowed: boolean;
  reload: () => void;
}) {
  const { t } = useTranslation("attributes");
  const [name, setName] = useState("");
  const [issue, setIssue] = useState<AttributeIssueKey | null>(null);
  const [added, setAdded] = useState<AttributeDefinition["options"]>([]);
  const [success, setSuccess] = useState(false);
  const [error, run, pending] = useAction(reload);
  if (definition.type !== "select" && definition.type !== "multiSelect") {
    return <section className="physical-panel"><h2>{t("options")}</h2><p className="hint">{t("noOptionsForType")}</p></section>;
  }
  const options = [...definition.options, ...added.filter((option) => !definition.options.some((current) => current.id === option.id))];
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextIssue = validateOption(name, options.map((option) => option.code));
    setIssue(nextIssue);
    setSuccess(false);
    if (nextIssue) {
      document.getElementById("attribute-option-name")?.focus();
      return;
    }
    await run(async () => {
      const updated = await api.addOption(storeId, definition.id, name.trim());
      setAdded(updated.options);
      setName("");
      setSuccess(true);
    });
  }
  return (
    <section className="physical-panel attribute-options">
      <h2>{t("options")}</h2>
      {options.length === 0 ? <p className="hint">{t("emptyOptions")}</p> : (
        <ul className="attribute-option-list">
          {options.map((option) => <li key={option.id}><strong>{option.name}</strong><code>{option.code}</code></li>)}
        </ul>
      )}
      {allowed && <form noValidate onSubmit={submit} className="attribute-option-form">
        <Field
          id="attribute-option-name"
          name="optionName"
          label={t("newOption")}
          hint={t("optionHint")}
          maxLength={200}
          value={name}
          error={issue ? t(`validation.${issue}`) : undefined}
          onChange={(event) => { setName(event.target.value); setIssue(null); setSuccess(false); }}
        />
        <Button type="submit" busy={pending} busyLabel={t("addingOption")}>{t("addOption")}</Button>
        {success && <InlineMessage tone="success">{t("optionAdded")}</InlineMessage>}
        {error !== null && <RequestError error={error} operation="write" />}
      </form>}
    </section>
  );
}
