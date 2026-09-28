import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { ProductSummary } from "../api";
import { formatPrice, useStore } from "../storeContext";
import { Stars } from "./Stars";

export function ProductCard({ product }: { product: ProductSummary }) {
  const { t } = useTranslation("catalog");
  const store = useStore();
  return (
    <Link to={`/p/${product.slug}`} className="product-card">
      {product.imageUrl ? (
        <img
          src={product.imageUrl}
          alt={product.name}
          className="product-image"
          loading="lazy"
          width={480}
          height={360}
        />
      ) : (
        <div
          className="product-image product-image-placeholder"
          aria-hidden="true"
        />
      )}
      <span className="product-name" lang={store.culture}>
        {product.name}
      </span>
      <span className="product-price">{formatPrice(product.price, store)}</span>
      {product.reviewCount > 0 && (
        <Stars rating={product.rating} count={product.reviewCount} />
      )}
      {product.available === 0 && (
        <span className="hint">{t("outOfStock")}</span>
      )}
    </Link>
  );
}
