import { useId } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext, useParams, useSearchParams } from "react-router";
import { getProducts, type Category, type Facet } from "../api";
import { statusOf } from "../api/errors";
import { ActiveFilters } from "../components/filters/ActiveFilters";
import { FilterPanel } from "../components/filters/FilterPanel";
import { MobileFilterDialog } from "../components/filters/MobileFilterDialog";
import {
  activeFilterCount,
  facetsForQuery,
  filterKey,
  hasFilters,
  withParam,
  withoutCatalogQuery,
  withoutFilters,
} from "../components/filters/filterParams";
import { CatalogLoading } from "../components/catalog/CatalogLoading";
import { CatalogHeader } from "../components/catalog/CatalogHeader";
import { Pagination } from "../components/catalog/Pagination";
import { ProductCard } from "../components/ProductCard";
import { HomeDiscovery } from "../components/HomeDiscovery";
import { EmptyState } from "../components/ui/EmptyState";
import { InlineMessage } from "../components/ui/InlineMessage";
import { RequestError } from "../components/ui/RequestError";
import { useRequest } from "../useRequest";
import { useStore } from "../storeContext";

export function ProductListPage() {
  const { t } = useTranslation(["catalog", "errors", "navigation"]);
  const store = useStore();
  const { slug } = useParams();
  const categories = useOutletContext<Category[]>();
  const [searchParams, setSearchParams] = useSearchParams();
  const sortId = useId();
  const categorySlug = slug ?? searchParams.get("category") ?? undefined;
  const scope = categorySlug ?? "";
  const products = useRequest(
    `products:${scope}?${searchParams}`,
    (signal) => getProducts(slug, searchParams, signal),
    {
      retainPrevious: (previousKey) =>
        previousKey.startsWith(`products:${scope}?`),
    },
  );
  const retainedData = products.transitionData ?? null;
  // Keep the established root catalog URLs. A filtered/sorted/paged visit opens directly
  // on its results, while a fresh home visit adds the store's discovery entry points.
  const showDiscovery = !slug && !Array.from(searchParams.keys()).some(
    (key) => key === "page" || key === "pageSize" || key === "sort" || key === "category" || key === "q" || key.startsWith("f."),
  );
  const currentData = products.status === "ready" ? products.data : retainedData;
  const displayTitle = categorySlug
    ? (currentData?.path.at(-1)?.name ?? categories.find((category) => category.slug === categorySlug)?.name ?? t("navigation:products"))
    : t("navigation:allProducts");
  const pageHeading = (
    <>
      {showDiscovery && (
        <HomeDiscovery categories={categories} product={currentData?.items.find((product) => product.imageUrl)} />
      )}
      <CatalogHeader
        title={displayTitle}
        category={Boolean(categorySlug)}
        path={currentData?.path ?? []}
        subcategories={currentData?.children ?? []}
        pageText={currentData?.pageText ?? null}
        headingLevel={showDiscovery ? 2 : 1}
      />
    </>
  );

  function changeFilter(code: string, value: string | null) {
    setSearchParams(withParam(searchParams, filterKey(code), value));
  }

  function clearFilters() {
    setSearchParams(withoutFilters(searchParams));
  }

  function sortOptions(facets: Facet[]): [string, string][] {
    const attributeSorts = facets
      .filter(
        (facet) =>
          facet.type === "integer" ||
          facet.type === "decimal" ||
          facet.type === "date",
      )
      .flatMap((facet): [string, string][] => [
        [
          `attr.${facet.code}`,
          t("catalog:attributeLowHigh", { name: facet.name }),
        ],
        [
          `-attr.${facet.code}`,
          t("catalog:attributeHighLow", { name: facet.name }),
        ],
      ]);
    return [
      ["", t("catalog:defaultOrder")],
      ["price", t("catalog:priceLowHigh")],
      ["-price", t("catalog:priceHighLow")],
      ["name", t("catalog:nameAZ")],
      ["-name", t("catalog:nameZA")],
      ["-rating", t("catalog:ratingHighLow")],
      ...attributeSorts,
    ];
  }

  if (products.status === "loading" && retainedData === null) {
    return (
      <>
        {pageHeading}
        <CatalogLoading label={t("catalog:loadingProducts")} />
      </>
    );
  }

  if (products.status === "not-found") {
    return (
      <EmptyState title={t("catalog:categoryNotFoundTitle")}>
        <p>{t("catalog:categoryNotFoundBody")}</p>
        <p>
          <Link to="/">{t("catalog:browseAll")}</Link>
        </p>
      </EmptyState>
    );
  }

  if (products.status === "error") {
    const invalidQuery = statusOf(products.error) === 400;
    return (
      <>
        {pageHeading}
        {invalidQuery && (
          <InlineMessage tone="error" title={t("catalog:invalidQueryTitle")}>
            <p>{t("catalog:invalidQueryBody")}</p>
            <button
              type="button"
              onClick={() => setSearchParams(withoutCatalogQuery(searchParams))}
            >
              {t("catalog:removeInvalidQuery")}
            </button>
          </InlineMessage>
        )}
        <RequestError
          error={products.error}
          operation="read"
          onRetry={invalidQuery ? undefined : products.reload}
        />
      </>
    );
  }

  const data =
    products.status === "ready" ? products.data : retainedData!;
  const isRefreshing =
    products.status === "loading" ||
    (products.status === "ready" && products.refreshing);
  const refreshError =
    products.status === "ready" ? products.refreshError : null;
  const facets = facetsForQuery(data.filters, searchParams);
  const selectedCount = activeFilterCount(facets);
  const pageCount = Math.max(Math.ceil(data.totalCount / data.pageSize), 1);
  const outOfRange = data.page > pageCount && data.page > 1;
  const filtered = hasFilters(searchParams);

  return (
    <>
      {pageHeading}
      {refreshError !== null && (
        <RequestError
          error={refreshError}
          operation="read"
          onRetry={products.reload}
        />
      )}
      <div className={`catalog${facets.length === 0 ? " catalog-without-filters" : ""}`}>
        {facets.length > 0 && <aside className="catalog-filter-rail">
          <FilterPanel
            facets={facets}
            onChange={changeFilter}
            onClear={clearFilters}
            contentLanguage={store.culture}
          />
        </aside>}
        <section
          className="catalog-results"
          aria-labelledby="catalog-result-summary"
          aria-busy={isRefreshing || undefined}
        >
          <div className="catalog-toolbar">
            <div className="catalog-result-summary">
              <span
                id="catalog-result-summary"
                role="status"
                aria-live="polite"
                aria-atomic="true"
              >
                {t("catalog:productCount", { count: data.totalCount })}
                {isRefreshing && (
                  <span className="refreshing-label">
                    {" "}
                    {t("catalog:refreshing")}
                  </span>
                )}
              </span>
              <MobileFilterDialog
                facets={facets}
                activeCount={selectedCount}
                onChange={changeFilter}
                onClear={clearFilters}
                resultCount={data.totalCount}
                refreshing={isRefreshing}
                contentLanguage={store.culture}
              />
            </div>
            <label className="catalog-sort" htmlFor={sortId}>
              <span>{t("catalog:sortBy")}</span>
              <select
                id={sortId}
                value={searchParams.get("sort") ?? ""}
                onChange={(event) =>
                  setSearchParams(
                    withParam(searchParams, "sort", event.target.value),
                  )
                }
              >
                {sortOptions(facets).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <ActiveFilters
            facets={facets}
            onChange={changeFilter}
            onClear={clearFilters}
          />

          <div className={isRefreshing ? "catalog-content refreshing" : "catalog-content"}>
            {outOfRange ? (
              <EmptyState
                title={t("catalog:pageOutOfRangeTitle")}
                headingLevel={2}
                action={
                  <button
                    type="button"
                    onClick={() =>
                      setSearchParams(withParam(searchParams, "page", null))
                    }
                  >
                    {t("catalog:returnToFirstPage")}
                  </button>
                }
              >
                <p>{t("catalog:pageOutOfRangeBody")}</p>
              </EmptyState>
            ) : data.items.length === 0 ? (
              <CatalogEmptyState
                category={Boolean(categorySlug)}
                filtered={filtered}
                clearFilters={clearFilters}
              />
            ) : (
              <div className="product-grid">
                {data.items.map((product, index) => (
                  <ProductCard key={product.id} product={product} priority={index === 0 && !showDiscovery} />
                ))}
              </div>
            )}
          </div>

          {!outOfRange && data.items.length > 0 && (
            <Pagination
              page={data.page}
              pageCount={pageCount}
              searchParams={searchParams}
            />
          )}
        </section>
      </div>
    </>
  );
}

function CatalogEmptyState({
  category,
  filtered,
  clearFilters,
}: {
  category: boolean;
  filtered: boolean;
  clearFilters: () => void;
}) {
  const { t } = useTranslation("catalog");
  if (filtered) {
    return (
      <EmptyState
        title={t("noMatchesTitle")}
        headingLevel={2}
        action={
          <button type="button" onClick={clearFilters}>
            {t("clearFilters")}
          </button>
        }
      >
        <p>{t("noMatches")}</p>
      </EmptyState>
    );
  }
  if (category) {
    return (
      <EmptyState title={t("emptyCategoryTitle")} headingLevel={2}>
        <p>{t("emptyCategoryBody")}</p>
        <p>
          <Link to="/">{t("browseAll")}</Link>
        </p>
      </EmptyState>
    );
  }
  return (
    <EmptyState title={t("emptyCatalogTitle")} headingLevel={2}>
      <p>{t("emptyCatalogBody")}</p>
    </EmptyState>
  );
}
