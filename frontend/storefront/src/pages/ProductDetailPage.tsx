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
      return (
        <>
          {product.refreshError !== null && (
            <RequestError
              error={product.refreshError}
              operation="read"
              onRetry={product.reload}
            />
          )}
          <article className="product-detail">
            <ProductGallery key={id} images={images} productName={name} />
            <div className="product-info">
              {categories.length > 0 && (
                <nav
                  className="product-categories"
                  aria-label={t("catalog:productCategories")}
                >
                  {categories.map((category) => (
                    <Link
                      key={category.slug}
                      to={categoryPath(category.slug)}
                      lang={store.culture}
                    >
                      {category.name}
                    </Link>
                  ))}
                </nav>
              )}
              <h1 lang={store.culture}>{name}</h1>
              <p className="product-price">{formatPrice(price, store)}</p>
              <Stars rating={rating} count={reviewCount} />
              <ProductPurchase key={id} product={product.data} />
              {description && (
                <section className="product-section" aria-labelledby="product-description-heading">
                  <h2 id="product-description-heading">{t("catalog:description")}</h2>
                  <p lang={store.culture}>{description}</p>
                </section>
              )}
              <ProductAttributes attributes={attributes} store={store} />
            </div>
          </article>
          <ProductReviews slug={product.data.slug} />
        </>
      );
    }
  }
}
