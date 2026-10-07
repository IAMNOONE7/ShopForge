import { useTranslation } from "react-i18next";
import type { Facet } from "../../api";
import { optionValueWithout } from "./filterParams";
import { useStore } from "../../storeContext";

type Props = {
  facets: Facet[];
  onChange: (code: string, value: string | null) => void;
  onClear: () => void;
};

export function ActiveFilters({ facets, onChange, onClear }: Props) {
  const { t } = useTranslation(["catalog", "common"]);
  const store = useStore();
  function bound(value: number | string) {
    const number = Number(value);
    return Number.isFinite(number)
      ? new Intl.NumberFormat(store.culture, { maximumFractionDigits: 20 }).format(number)
      : String(value);
  }
  const filters = facets.flatMap((facet) => {
    if (facet.options) {
      return facet.options
        .filter((option) => option.selected)
        .map((option) => ({
          key: `${facet.code}:${option.code}`,
          label: t("catalog:activeFilter", {
            name: facet.name,
            value: option.name,
          }),
          remove: () =>
            onChange(facet.code, optionValueWithout(facet, option.code)),
        }));
    }
    if (facet.selected !== null) {
      return [
        {
          key: facet.code,
          label: t("catalog:activeFilter", {
            name: facet.name,
            value: t(facet.selected ? "common:yes" : "common:no"),
          }),
          remove: () => onChange(facet.code, null),
        },
      ];
    }
    if (facet.selectedMin !== null || facet.selectedMax !== null) {
      const unit = facet.unit ? ` ${facet.unit}` : "";
      const value =
        facet.selectedMin !== null && facet.selectedMax !== null
          ? t("catalog:activeRange", {
              from: `${bound(facet.selectedMin)}${unit}`,
              to: `${bound(facet.selectedMax)}${unit}`,
            })
          : facet.selectedMin !== null
            ? t("catalog:activeRangeFrom", {
                value: `${bound(facet.selectedMin)}${unit}`,
              })
            : t("catalog:activeRangeTo", {
                value: `${bound(facet.selectedMax!)}${unit}`,
              });
      return [
        {
          key: facet.code,
          label: t("catalog:activeFilter", { name: facet.name, value }),
          remove: () => onChange(facet.code, null),
        },
      ];
    }
    return [];
  });

  if (filters.length === 0) return null;

  return (
    <section className="active-filters" aria-label={t("catalog:activeFilters")}>
      <ul>
        {filters.map((filter) => (
          <li key={filter.key}>
            <button
              type="button"
              className="filter-chip"
              aria-label={t("catalog:removeFilter", { filter: filter.label })}
              onClick={filter.remove}
            >
              <span>{filter.label}</span>
              <span aria-hidden="true">×</span>
            </button>
          </li>
        ))}
      </ul>
      <button type="button" className="link-button" onClick={onClear}>
        {t("catalog:clearFilters")}
      </button>
    </section>
  );
}
