import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { Category, ProductSummary } from "../api";
import { productPath } from "../publicPages";
import { useStore } from "../storeContext";
import { CategoryDirectory } from "./CategoryDirectory";
import { CurrencyValue } from "./CurrencyValue";

export function HomeDiscovery({
  categories,
  product,
}: {
  categories: Category[];
  product?: ProductSummary;
}) {
  const { t } = useTranslation("discovery");
  const store = useStore();

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
      <CategoryDirectory categories={categories} />
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
        <strong>
          <CurrencyValue value={product.price} culture={store.culture} currency={store.currency} />
        </strong>
        <span aria-hidden="true">↗</span>
      </span>
    </Link>
  );
}
