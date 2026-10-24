import { useId } from "react";
import { useTranslation } from "react-i18next";
import { measurementFields, type PackageDraft } from "./productValidation";
import { Field } from "./ui/Field";

export function PackageFields({ draft, issues, onChange }: {
  draft: PackageDraft;
  issues: readonly { field: string }[];
  onChange: {
    (field: typeof measurementFields[number], value: string): void;
    (field: "shipsAlone", value: boolean): void;
  };
}) {
  const { t } = useTranslation("products");
  const aloneHint = useId();
  return (
    <fieldset className="package-fields">
      <legend>{t("packageTitle")}</legend>
      <p className="hint">{t("packageHint")}</p>
      <div className="physical-fields-row">
        {measurementFields.map((field) => <Field key={field} name={field} label={t(field)}
          required inputMode="numeric" value={draft[field]}
          hint={t(field === "weightGrams" ? "weightHint" : "sizeHint")}
          error={issues.some((issue) => issue.field === field)
            ? t(field === "weightGrams" ? "validation.weightInvalid" : "validation.sizeInvalid") : undefined}
          onChange={(event) => onChange(field, event.target.value)} />)}
      </div>
      <div className="package-alone">
        <label><input type="checkbox" name="shipsAlone" checked={draft.shipsAlone}
          aria-describedby={aloneHint} onChange={(event) => onChange("shipsAlone", event.target.checked)} />
          <span>{t("shipsAlone")}</span></label>
        <p id={aloneHint} className="hint">{t("shipsAloneHint")}</p>
      </div>
    </fieldset>
  );
}
