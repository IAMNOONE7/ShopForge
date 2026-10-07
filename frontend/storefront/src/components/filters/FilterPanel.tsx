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
  contentLanguage?: string;
};

export function FilterPanel({
  facets,
  onChange,
  onClear,
  showHeading = true,
  contentLanguage,
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
      {showHeading && (
        <div className="filter-panel-heading">
          <h2 id={headingId}>{t("filters")}</h2>
          {hasSelection && <button type="button" className="link-button" onClick={onClear}>{t("clearFilters")}</button>}
        </div>
      )}
      {facets.map((facet) => (
        <fieldset key={facet.code}>
          <legend lang={contentLanguage}>
            {facet.name}
            {facet.unit && ` (${facet.unit})`}
          </legend>
          {facet.type === "select" || facet.type === "multiSelect" ? (
            <OptionsFilter
              facet={facet}
              onChange={(value) => onChange(facet.code, value)}
              contentLanguage={contentLanguage}
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
      {hasSelection && !showHeading && (
        <button type="button" className="link-button" onClick={onClear}>
          {t("clearFilters")}
        </button>
      )}
    </div>
  );
}
