import { useTranslation } from "react-i18next";
import type { AddressDraft } from "./address";

export function AddressFields({
  prefix,
  value,
  onChange,
  showErrors,
}: {
  prefix: "billing" | "shipping";
  value: AddressDraft;
  onChange: (value: AddressDraft) => void;
  showErrors: boolean;
}) {
  const { t } = useTranslation(["checkout", "validation"]);

  function field(
    name: keyof AddressDraft,
    label: string,
    autoComplete: string,
    options: {
      optional?: boolean;
      maxLength?: number;
      hint?: string;
    } = {},
  ) {
    const id = prefix + "-" + name;
    const valid =
      options.optional ||
      (name === "country"
        ? value.country.trim().length === 2
        : value[name].trim().length > 0);
    const hintId = options.hint ? id + "-hint" : undefined;

    return (
      <label
        className={
          "checkout-address-field " +
          (name === "line1" || name === "line2" ? "wide" : "")
        }
      >
        <span>
          {label}
          {options.optional && (
            <span className="optional"> {t("checkout:optional")}</span>
          )}
        </span>
        <input
          id={id}
          name={prefix + "." + name}
          value={value[name]}
          autoComplete={prefix + " " + autoComplete}
          maxLength={options.maxLength}
          required={!options.optional}
          aria-invalid={(showErrors && !valid) || undefined}
          aria-describedby={hintId}
          onChange={(event) =>
            onChange({ ...value, [name]: event.target.value })
          }
        />
        {options.hint && (
          <span id={hintId} className="hint">
            {options.hint}
          </span>
        )}
      </label>
    );
  }

  return (
    <div className="checkout-address-grid">
      {field("fullName", t("checkout:fullName"), "name")}
      {field("line1", t("checkout:street"), "address-line1")}
      {field("line2", t("checkout:line2"), "address-line2", {
        optional: true,
      })}
      {field("city", t("checkout:city"), "address-level2")}
      {field("postalCode", t("checkout:postalCode"), "postal-code")}
      {field("country", t("checkout:country"), "country", {
        maxLength: 2,
        hint: t("validation:countryCode", {
          example: t("checkout:countryExample"),
        }),
      })}
    </div>
  );
}
