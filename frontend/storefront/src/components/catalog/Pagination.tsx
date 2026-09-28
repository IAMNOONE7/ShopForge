import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { withParam } from "../filters/filterParams";

export function Pagination({
  page,
  pageCount,
  searchParams,
}: {
  page: number;
  pageCount: number;
  searchParams: URLSearchParams;
}) {
  const { t } = useTranslation("catalog");
  if (pageCount <= 1) return null;

  function to(target: number) {
    const next = withParam(
      searchParams,
      "page",
      target === 1 ? null : String(target),
    );
    const search = next.toString();
    return { search: search ? `?${search}` : "" };
  }

  return (
    <nav className="pagination" aria-label={t("pages")}>
      {page > 1 ? (
        <Link className="pagination-direction" to={to(page - 1)}>
          {t("previous")}
        </Link>
      ) : (
        <span className="pagination-direction disabled" aria-disabled="true">
          {t("previous")}
        </span>
      )}
      <ol>
        {pageItems(page, pageCount).map((item, index) =>
          item === "ellipsis" ? (
            <li key={`ellipsis-${index}`} aria-hidden="true">
              …
            </li>
          ) : (
            <li key={item}>
              <Link
                to={to(item)}
                aria-current={item === page ? "page" : undefined}
                aria-label={t("goToPage", { page: item })}
              >
                {item}
              </Link>
            </li>
          ),
        )}
      </ol>
      {page < pageCount ? (
        <Link className="pagination-direction" to={to(page + 1)}>
          {t("next")}
        </Link>
      ) : (
        <span className="pagination-direction disabled" aria-disabled="true">
          {t("next")}
        </span>
      )}
      <span className="sr-only">{t("pageOf", { page, pageCount })}</span>
    </nav>
  );
}

function pageItems(
  page: number,
  pageCount: number,
): (number | "ellipsis")[] {
  if (pageCount <= 7)
    return Array.from({ length: pageCount }, (_, index) => index + 1);
  const pages = new Set([1, pageCount, page - 1, page, page + 1]);
  const sorted = [...pages]
    .filter((value) => value >= 1 && value <= pageCount)
    .sort((left, right) => left - right);
  const result: (number | "ellipsis")[] = [];
  for (const value of sorted) {
    const previous = result.at(-1);
    if (typeof previous === "number" && value - previous > 1)
      result.push("ellipsis");
    result.push(value);
  }
  return result;
}
