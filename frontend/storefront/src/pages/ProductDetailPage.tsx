import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { getProduct } from "../api";
import { Message } from "../components/Message";
import { ProductReviews } from "../components/ProductReviews";
import { Stars } from "../components/Stars";
import { ProductAttributes } from "../components/product/ProductAttributes";
import { ProductDetailLoading } from "../components/product/ProductDetailLoading";
import { ProductGallery } from "../components/product/ProductGallery";
import { ProductPurchase } from "../components/product/ProductPurchase";
import { RequestError } from "../components/ui/RequestError";
import { formatPrice, useStore } from "../storeContext";
import { useRequest } from "../useRequest";
import { categoryPath } from "../publicPages";

export function ProductDetailPage() {
  const { t } = useTranslation(["catalog", "errors"]);
  const { slug = "" } = useParams();
  const store = useStore();
  const product = useRequest(`product:${slug}`, (signal) =>
    getProduct(slug, signal),
  );

  switch (product.status) {
    case "loading":
      return <ProductDetailLoading label={t("catalog:loadingProduct")} />;
    case "not-found":
      return (
        <Message
          title={t("catalog:productNotFoundTitle")}
          text={t("catalog:productNotFoundBody")}
        />
      );
    case "error":
      return (
        <section className="product-load-error">
          <h1>{t("catalog:productUnavailableTitle")}</h1>
          <RequestError
            error={product.error}
            operation="read"
            onRetry={product.reload}
          />
        </section>
      );
    case "ready": {
      const {
        id,
        name,
        price,
        rating,
        reviewCount,
        description,
        images,
        categories,
        attributes,
      } = product.data;
      const category = [...categories].sort((left, right) => right.path.length - left.path.length)[0];
      const path = category?.path.length ? category.path : category ? [category] : [];
      return (
        <>
          {product.refreshError !== null && (
            <RequestError
              error={product.refreshError}
              operation="read"
              onRetry={product.reload}
            />
          )}
          <article className="product-page">
            <nav className="catalog-breadcrumbs product-breadcrumbs" aria-label={t("catalog:productBreadcrumbs")}>
              <ol>
                <li className={path.length === 0 ? "product-breadcrumb-parent" : undefined}>
                  <Link to="/">{t("catalog:browseAll")}</Link>
                </li>
                {path.map((crumb, index) => (
                  <li key={crumb.slug} className={index === path.length - 1 ? "product-breadcrumb-parent" : undefined}>
                    <Link to={categoryPath(crumb.slug)} lang={store.culture}>{crumb.name}</Link>
                  </li>
                ))}
                <li><span aria-current="page" lang={store.culture}>{name}</span></li>
              </ol>
            </nav>
            <div className="product-detail">
              <header className="product-summary">
                <h1 lang={store.culture}>{name}</h1>
                <p className="product-price">{formatPrice(price, store)}</p>
                {reviewCount > 0 ? (
                  <a className="product-review-link" href="#product-reviews-heading">
                    <Stars rating={rating} count={reviewCount} />
                  </a>
                ) : <Stars rating={rating} count={reviewCount} />}
              </header>
              <ProductGallery key={id} images={images} productName={name} contentLanguage={store.culture} />
              <ProductPurchase key={id} product={product.data} />
              {(description || attributes.length > 0) && (
                <nav className="product-section-links" aria-label={t("catalog:productInformation")}>
                  {description && <a href="#product-description-heading">{t("catalog:description")}</a>}
                  {attributes.length > 0 && <a href="#product-details-heading">{t("catalog:productDetails")}</a>}
                </nav>
              )}
            </div>
            {(description || attributes.length > 0) && (
              <div className="product-information">
                {description && (
                  <section className="product-section product-description" aria-labelledby="product-description-heading">
                    <h2 id="product-description-heading" tabIndex={-1}>{t("catalog:description")}</h2>
                    <div lang={store.culture}>
                      {description.split(/\n\s*\n/).filter((paragraph) => paragraph.trim()).map((paragraph, index) => (
                        <p key={index}>{paragraph}</p>
                      ))}
                    </div>
                  </section>
                )}
                <ProductAttributes attributes={attributes} store={store} />
              </div>
            )}
            {categories.length > 1 && (
              <nav className="product-categories product-extra-categories" aria-label={t("catalog:productCategories")}>
                {categories.map((assigned) => (
                  <Link key={assigned.slug} to={categoryPath(assigned.slug)} lang={store.culture}>{assigned.name}</Link>
                ))}
              </nav>
            )}
            <ProductReviews key={product.data.slug} slug={product.data.slug} reviewCount={reviewCount} />
          </article>
        </>
      );
    }
  }
}
