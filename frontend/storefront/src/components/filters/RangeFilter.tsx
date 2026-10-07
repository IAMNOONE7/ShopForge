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
  const selection = `${facet.selectedMin ?? ""}:${facet.selectedMax ?? ""}`;
  const [draft, setDraft] = useState({
    selection,
    minimum: String(facet.selectedMin ?? ""),
    maximum: String(facet.selectedMax ?? ""),
    invalid: false,
  });
  // Applied URL changes reset the draft without replacing a focused input. A
  // same-range refresh keeps unfinished typing intact.
  if (draft.selection !== selection) {
    setDraft({
      selection,
      minimum: String(facet.selectedMin ?? ""),
      maximum: String(facet.selectedMax ?? ""),
      invalid: false,
    });
  }
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
      setDraft((current) => ({ ...current, invalid: true }));
      return;
    }

    setDraft((current) => ({ ...current, invalid: false }));
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
    <form className="range-filter" noValidate onSubmit={submit}>
      <label className="range-bound">
        <span>{t("catalog:rangeMinimum")}</span>
        <input
          name="min"
          type={inputType}
          step={step}
          placeholder={String(facet.min ?? "")}
          value={draft.minimum}
          onChange={(event) => setDraft((current) => ({
            ...current,
            minimum: event.target.value,
            invalid: false,
          }))}
          aria-label={t("catalog:rangeFrom", { name: facet.name })}
          aria-invalid={draft.invalid || undefined}
          aria-describedby={draft.invalid ? errorId : undefined}
        />
      </label>
      <span aria-hidden="true">–</span>
      <label className="range-bound">
        <span>{t("catalog:rangeMaximum")}</span>
        <input
          name="max"
          type={inputType}
          step={step}
          placeholder={String(facet.max ?? "")}
          value={draft.maximum}
          onChange={(event) => setDraft((current) => ({
            ...current,
            maximum: event.target.value,
            invalid: false,
          }))}
          aria-label={t("catalog:rangeTo", { name: facet.name })}
          aria-invalid={draft.invalid || undefined}
          aria-describedby={draft.invalid ? errorId : undefined}
        />
      </label>
      {draft.invalid && (
        <span id={errorId} className="filter-validation" role="alert">
          {t("catalog:rangeInvalid")}
        </span>
      )}
      <button type="submit">{t("common:apply")}</button>
    </form>
  );
}
