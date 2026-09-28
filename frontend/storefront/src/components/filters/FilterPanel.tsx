import { useId } from "react";
import { useTranslation } from "react-i18next";
import type { Facet } from "../../api";
import { BooleanFilter } from "./BooleanFilter";
import { OptionsFilter } from "./OptionsFilter";
import { RangeFilter } from "./RangeFilter";

type FilterPanelProps = {
  facets: Facet[];
  onChange: (code: string, value: string | null) => void;
  onClear: () => void;
  showHeading?: boolean;
};

export function FilterPanel({
  facets,
  onChange,
  onClear,
  showHeading = true,
}: FilterPanelProps) {
  const { t } = useTranslation("catalog");
  const headingId = useId();
  if (facets.length === 0) return null;
  const hasSelection = facets.some(
    (facet) =>
      facet.selected !== null ||
      facet.selectedMin !== null ||
      facet.selectedMax !== null ||
      facet.options?.some((option) => option.selected),
  );

  return (
    <div
      className="filter-panel"
      aria-labelledby={showHeading ? headingId : undefined}
    >
      {showHeading && <h2 id={headingId}>{t("filters")}</h2>}
      {facets.map((facet) => (
        <fieldset key={facet.code}>
          <legend>
            {facet.name}
            {facet.unit && ` (${facet.unit})`}
          </legend>
          {facet.type === "select" || facet.type === "multiSelect" ? (
            <OptionsFilter
              facet={facet}
              onChange={(value) => onChange(facet.code, value)}
            />
          ) : facet.type === "boolean" ? (
            <BooleanFilter
              facet={facet}
              onChange={(value) => onChange(facet.code, value)}
            />
          ) : (
            <RangeFilter
              facet={facet}
              onChange={(value) => onChange(facet.code, value)}
            />
          )}
        </fieldset>
      ))}
      {hasSelection && (
        <button type="button" className="link-button" onClick={onClear}>
          {t("clearFilters")}
        </button>
      )}
    </div>
  );
}
