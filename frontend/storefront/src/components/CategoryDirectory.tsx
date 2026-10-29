import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { Category } from "../api";
import { categoryPath } from "../publicPages";
import { useStore } from "../storeContext";
import { categoryTree } from "./categoryTree";

export function CategoryDirectory({ categories }: { categories: Category[] }) {
  const { t } = useTranslation(["discovery", "navigation"]);
  const store = useStore();
  const roots = categoryTree(categories);
  if (roots.length === 0) return null;

  return <section className="category-directory" aria-labelledby="category-directory-title">
    <div className="discovery-section-heading">
      <h2 id="category-directory-title">{t("discovery:browseCategories")}</h2>
      <span className="eyebrow" aria-hidden="true">{String(roots.length).padStart(2, "0")}</span>
    </div>
    <ul className="category-directory-grid">
      {roots.map(({ category, children }) => <li key={category.slug} className="category-entry">
        <h3>
          <Link className="category-entry-title" to={categoryPath(category.slug)} lang={store.culture}>
            <span className="category-entry-name">{category.name}</span>
            <span className="category-entry-indicator" aria-hidden="true">↗</span>
          </Link>
        </h3>
        <div className="category-entry-links">
          {children.length > 0 ? (
            <ul className="category-entry-children" aria-label={t("navigation:subcategoriesOf", { name: category.name })}>
              {children.map(({ category: child }) => <li key={child.slug}>
                <Link to={categoryPath(child.slug)} lang={store.culture}>
                  {child.name}<span aria-hidden="true">→</span>
                </Link>
              </li>)}
            </ul>
          ) : (
            <Link className="category-entry-browse" to={categoryPath(category.slug)}
              aria-label={t("discovery:browseCategory", { name: category.name })}>
              {t("discovery:browseProducts")}<span aria-hidden="true">→</span>
            </Link>
          )}
        </div>
      </li>)}
    </ul>
  </section>;
}
