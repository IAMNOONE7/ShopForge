import type { Facet } from "../../api";

export const filterKey = (code: string) => `f.${code}`;

// Returns a copy with one value changed. Any filter or sort change starts again at page 1.
export function withParam(
  params: URLSearchParams,
  key: string,
  value: string | null,
) {
  const next = new URLSearchParams(params);
  if (value === null || value === "") next.delete(key);
  else next.set(key, value);
  if (key !== "page") next.delete("page");
  return next;
}

export function withoutCatalogQuery(params: URLSearchParams) {
  const next = new URLSearchParams(params);
  for (const key of [...next.keys()]) {
    if (key.startsWith("f.") || key === "sort" || key === "page") {
      next.delete(key);
    }
  }
  return next;
}

export function withoutFilters(params: URLSearchParams) {
  const next = new URLSearchParams(params);
  for (const key of [...next.keys()]) {
    if (key.startsWith("f.")) next.delete(key);
  }
  next.delete("page");
  return next;
}

export function hasFilters(params: URLSearchParams) {
  return [...params.keys()].some((key) => key.startsWith("f."));
}

export function optionValueWithout(
  facet: Facet,
  removedCode: string,
): string | null {
  const remaining = (facet.options ?? [])
    .filter((option) => option.selected && option.code !== removedCode)
    .map((option) => option.code);
  return remaining.length > 0 ? remaining.join(",") : null;
}

// While a URL-driven request is pending, keep the previous cards visible but make
// the controls reflect the new URL immediately rather than the previous response.
export function facetsForQuery(
  facets: Facet[],
  params: URLSearchParams,
): Facet[] {
  return facets.map((facet) => {
    const raw = params.get(filterKey(facet.code));
    if (facet.type === "select" || facet.type === "multiSelect") {
      const selected = new Set(
        raw?.split(",").map((value) => value.trim()).filter(Boolean) ?? [],
      );
      return {
        ...facet,
        options: (facet.options ?? []).map((option) => ({
          ...option,
          selected: selected.has(option.code),
        })),
      };
    }
    if (facet.type === "boolean") {
      return {
        ...facet,
        selected: raw === "true" ? true : raw === "false" ? false : null,
      };
    }
    const range = raw
      ? raw.includes("..")
        ? raw.split("..", 2)
        : [raw, raw]
      : [];
    const [selectedMin = "", selectedMax = ""] = range;
    return {
      ...facet,
      selectedMin: selectedMin || null,
      selectedMax: selectedMax || null,
    };
  });
}

export function activeFilterCount(facets: Facet[]) {
  return facets.reduce((count, facet) => {
    if (facet.options)
      return count + facet.options.filter((option) => option.selected).length;
    if (facet.selected !== null) return count + 1;
    if (facet.selectedMin !== null || facet.selectedMax !== null)
      return count + 1;
    return count;
  }, 0);
}
