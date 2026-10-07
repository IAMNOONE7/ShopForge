import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { Category, ProductSummary } from "../api";
import { categoryPath, productPath } from "../publicPages";
import { formatPrice, useStore } from "../storeContext";
import { categoryTree } from "./categoryTree";

export function HomeDiscovery({
  categories,
  product,
}: {
  categories: Category[];
  product?: ProductSummary;
}) {
  const { t } = useTranslation("discovery");
  const store = useStore();
  const roots = categoryTree(categories);

  return (
    <div className="home-discovery">
      <section className="discovery-intro" aria-labelledby="store-welcome">
        <div className="discovery-copy">
          <span className="eyebrow">{t("shop")}</span>
          <h1 id="store-welcome" lang={store.culture}>
            {store.name}
          </h1>
          <a className="discovery-action" href="#shop-products">
            {t("exploreProducts")} <span aria-hidden="true">↗</span>
          </a>
        </div>
        {product?.imageUrl && (
          <DiscoveryProduct
            key={product.imageUrl}
            product={product}
            imageUrl={product.imageUrl}
          />
        )}
      </section>
      {roots.length > 0 && (
        <section className="category-directory" aria-labelledby="category-directory-title">
          <div className="discovery-section-heading">
            <h2 id="category-directory-title">{t("browseCategories")}</h2>
            <span className="eyebrow" aria-hidden="true">
              {String(roots.length).padStart(2, "0")}
            </span>
          </div>
          <ul className="category-directory-grid">
            {roots.map(({ category, children }) => (
              <li key={category.slug} className="category-entry">
                <Link
                  className="category-entry-title"
                  to={categoryPath(category.slug)}
                  lang={store.culture}
                >
                  {category.name}
                  <span aria-hidden="true">↗</span>
                </Link>
                {children.length > 0 && (
                  <ul className="category-entry-children">
                    {children.map(({ category: child }) => (
                      <li key={child.slug}>
                        <Link to={categoryPath(child.slug)} lang={store.culture}>
                          {child.name}
                        </Link>
                      </li>
                    ))}
                  </ul>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function DiscoveryProduct({ product, imageUrl }: {
  product: ProductSummary;
  imageUrl: string;
}) {
  const { t } = useTranslation("discovery");
  const store = useStore();
  const [failed, setFailed] = useState(false);
  return (
    <Link to={productPath(product.slug)} className="discovery-product">
      <div className="discovery-product-media">
        {failed ? (
          <span className="hint">{t("imageUnavailable")}</span>
        ) : (
          <img
            src={imageUrl}
            alt=""
            width={640}
            height={480}
            fetchPriority="high"
            onError={() => setFailed(true)}
          />
        )}
      </div>
      <span className="discovery-product-caption">
        <span lang={store.culture}>{product.name}</span>
        <strong>{formatPrice(product.price, store)}</strong>
        <span aria-hidden="true">↗</span>
      </span>
    </Link>
  );
}
