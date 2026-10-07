import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { Category } from "../../api";
import { categoryPath } from "../../publicPages";
import { useStore } from "../../storeContext";

export function CatalogHeader({ title, category, path, subcategories, pageText, headingLevel }: {
  title: string;
  category: boolean;
  path: Category[];
  subcategories: Category[];
  pageText: string | null;
  headingLevel: 1 | 2;
}) {
  const { t } = useTranslation("catalog");
  const store = useStore();
  const Heading = headingLevel === 1 ? "h1" : "h2";

  return (
    <header className="catalog-page-header">
      {category && (
        <nav className="catalog-breadcrumbs" aria-label={t("breadcrumbs")}>
          <ol>
            <li><Link to="/">{t("browseAll")}</Link></li>
            {path.slice(0, -1).map((crumb) => (
              <li key={crumb.slug}>
                <Link to={categoryPath(crumb.slug)} lang={store.culture}>{crumb.name}</Link>
              </li>
            ))}
            <li><span aria-current="page" lang={store.culture}>{title}</span></li>
          </ol>
        </nav>
      )}
      <Heading id="shop-products" className="catalog-heading" tabIndex={-1} lang={category ? store.culture : undefined}>
        {title}
      </Heading>
      {pageText && (
        <div className="catalog-description" lang={store.culture}>
          {pageText.split(/\n\s*\n/).filter((paragraph) => paragraph.trim()).map((paragraph, index) => (
            <p key={index}>{paragraph}</p>
          ))}
        </div>
      )}
      {subcategories.length > 0 && (
        <nav className="catalog-subcategories" aria-label={t("subcategories")}>
          <ul>
            {subcategories.map((child) => (
              <li key={child.slug}>
                <Link to={categoryPath(child.slug)} lang={store.culture}>
                  {child.name}<span aria-hidden="true">↗</span>
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      )}
    </header>
  );
}
