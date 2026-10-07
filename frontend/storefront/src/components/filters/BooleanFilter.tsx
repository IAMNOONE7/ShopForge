import { useId } from "react";
import { useTranslation } from "react-i18next";
import type { Facet } from "../../api";

export function BooleanFilter({
  facet,
  onChange,
}: {
  facet: Facet;
  onChange: (value: string | null) => void;
}) {
  const { t } = useTranslation(["catalog", "common"]);
  const name = `boolean-${useId()}`;
  const choices: {
    value: "any" | "true" | "false";
    label: string;
    count: number | null;
    selected: boolean;
  }[] = [
    {
      value: "any",
      label: t("catalog:anyValue"),
      count: null,
      selected: facet.selected === null,
    },
    {
      value: "true",
      label: t("common:yes"),
      count: facet.trueCount,
      selected: facet.selected === true,
    },
    {
      value: "false",
      label: t("common:no"),
      count: facet.falseCount,
      selected: facet.selected === false,
    },
  ];

  return (
    <div className="filter-options">
      {choices.map((choice) => (
        <label key={choice.value} className="filter-option">
          <input
            type="radio"
            name={name}
            value={choice.value}
            checked={choice.selected}
            disabled={choice.count === 0 && !choice.selected}
            onChange={() =>
              onChange(choice.value === "any" ? null : choice.value)
            }
          />
          <span className="filter-option-name">{choice.label}</span>{" "}
          {choice.count !== null && (
            <span className="filter-count">({choice.count})</span>
          )}
        </label>
      ))}
    </div>
  );
}
