import { useTranslation } from "react-i18next";
import type { AttributeDefinition, AttributeValues } from "../api";
import { fieldName } from "./attributeValues";
export function AttributeValueFields({
  attributes,
  values,
}: {
  attributes: AttributeDefinition[];
  values: AttributeValues;
}) {
  const { t } = useTranslation(["attributes", "common"]);
  return (
    <fieldset>
      <legend>{t("attributes:legend")}</legend>
      {attributes.map((attribute) => {
        const value = values[attribute.code];
        const label = `${attribute.name}${attribute.unit ? ` (${attribute.unit})` : ""}`;
        switch (attribute.type) {
          case "multiSelect":
            return (
              <div key={attribute.code} className="attribute-field">
                <span>{label}</span>
                {attribute.options.map((option) => (
                  <label key={option.code}>
                    <input
                      type="checkbox"
                      name={fieldName(attribute.code)}
                      value={option.code}
                      defaultChecked={
                        Array.isArray(value) && value.includes(option.code)
                      }
                    />{" "}
                    {option.name}
                  </label>
                ))}
              </div>
            );
          case "select":
            return (
              <label key={attribute.code}>
                {label}
                <select
                  name={fieldName(attribute.code)}
                  defaultValue={typeof value === "string" ? value : ""}
                >
                  <option value="">—</option>
                  {attribute.options.map((option) => (
                    <option key={option.code} value={option.code}>
                      {option.name}
                    </option>
                  ))}
                </select>
              </label>
            );
          case "boolean":
            return (
              <label key={attribute.code}>
                {label}
                <select
                  name={fieldName(attribute.code)}
                  defaultValue={value === undefined ? "" : String(value)}
                >
                  <option value="">—</option>
                  <option value="true">{t("common:yes")}</option>
                  <option value="false">{t("common:no")}</option>
                </select>
              </label>
            );
          default:
            return (
              <label key={attribute.code}>
                {label}
                <input
                  name={fieldName(attribute.code)}
                  type={
                    attribute.type === "text"
                      ? "text"
                      : attribute.type === "date"
                        ? "date"
                        : "number"
                  }
                  step={attribute.type === "decimal" ? "any" : undefined}
                  defaultValue={value === undefined ? "" : String(value)}
                />
              </label>
            );
        }
      })}
    </fieldset>
  );
}
