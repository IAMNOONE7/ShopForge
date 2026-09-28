import { useId, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { Facet } from "../../api";

export function RangeFilter({
  facet,
  onChange,
}: {
  facet: Facet;
  onChange: (value: string | null) => void;
}) {
  const { t } = useTranslation(["catalog", "common"]);
  const [invalid, setInvalid] = useState(false);
  const errorId = useId();
  const inputType = facet.type === "date" ? "date" : "number";
  const step = facet.type === "decimal" ? "any" : undefined;

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const minimum = String(form.get("min") ?? "").trim();
    const maximum = String(form.get("max") ?? "").trim();
    const outOfOrder =
      minimum !== "" &&
      maximum !== "" &&
      (facet.type === "date"
        ? minimum > maximum
        : Number(minimum) > Number(maximum));
    const malformed =
      inputType === "number" &&
      [minimum, maximum].some(
        (value) => value !== "" && !Number.isFinite(Number(value)),
      );

    if (outOfOrder || malformed) {
      setInvalid(true);
      return;
    }

    setInvalid(false);
    onChange(minimum || maximum ? `${minimum}..${maximum}` : null);
  }

  if (
    facet.min === null &&
    facet.selectedMin === null &&
    facet.selectedMax === null
  ) {
    return <p className="filter-count">{t("catalog:noValues")}</p>;
  }

  return (
    <form
      className="range-filter"
      key={`${facet.selectedMin}-${facet.selectedMax}`}
      noValidate
      onSubmit={submit}
    >
      <input
        name="min"
        type={inputType}
        step={step}
        placeholder={String(facet.min ?? "")}
        defaultValue={facet.selectedMin ?? ""}
        aria-label={t("catalog:rangeFrom", { name: facet.name })}
        aria-invalid={invalid || undefined}
        aria-describedby={invalid ? errorId : undefined}
      />
      <span aria-hidden="true">–</span>
      <input
        name="max"
        type={inputType}
        step={step}
        placeholder={String(facet.max ?? "")}
        defaultValue={facet.selectedMax ?? ""}
        aria-label={t("catalog:rangeTo", { name: facet.name })}
        aria-invalid={invalid || undefined}
        aria-describedby={invalid ? errorId : undefined}
      />
      {invalid && (
        <span id={errorId} className="filter-validation" role="alert">
          {t("catalog:rangeInvalid")}
        </span>
      )}
      <button type="submit">{t("common:apply")}</button>
    </form>
  );
}
