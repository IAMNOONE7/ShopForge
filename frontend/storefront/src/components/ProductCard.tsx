import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { ProductSummary } from "../api";
import { formatPrice, useStore } from "../storeContext";
import { Stars } from "./Stars";
import { productPath } from "../publicPages";

export function ProductCard({ product, priority = false }: { product: ProductSummary; priority?: boolean }) {
  const { t } = useTranslation("catalog");
  const store = useStore();
  return (
    <Link to={productPath(product.slug)} className="product-card">
      <div className="product-card-media">
        <CardImage key={product.imageUrl} url={product.imageUrl} priority={priority} />
      </div>
      <div className="product-card-body">
        <span className="product-name" lang={store.culture}>{product.name}</span>
        <span className="product-price">{formatPrice(product.price, store)}</span>
        {product.reviewCount > 0 && <Stars rating={product.rating} count={product.reviewCount} />}
        <span className={`product-card-availability ${product.available > 0 ? "available" : "unavailable"}`}>
          <span>{t(product.available > 0 ? "inStock" : "outOfStock")}</span>
          <span aria-hidden="true">↗</span>
        </span>
      </div>
    </Link>
  );
}

function CardImage({ url, priority }: { url: string | null; priority: boolean }) {
  const { t } = useTranslation("catalog");
  const [failed, setFailed] = useState(false);
  if (!url || failed) {
    return (
      <span className="product-card-placeholder" aria-hidden="true">
        <svg viewBox="0 0 32 32" fill="none" stroke="currentColor" strokeWidth="1" aria-hidden="true">
          <rect x="4.5" y="4.5" width="23" height="23" rx="2" />
          <path d="M5 24l8-9 6 6 4-5 4 4M3 3l26 26" />
        </svg>
        <span>{t("imageUnavailable")}</span>
      </span>
    );
  }
  return (
    <img
      src={url}
      alt=""
      loading={priority ? "eager" : "lazy"}
      fetchPriority={priority ? "high" : "auto"}
      width={480}
      height={480}
      onError={() => setFailed(true)}
    />
  );
}
