import { useCallback, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";
import type { Category, ProductSummary } from "../api";
import { useStore } from "../storeContext";
import { HomeDiscovery } from "./HomeDiscovery";
import { LoadingState } from "./ui/LoadingState";

export function HomeEntry({
  categories, categoriesPending, productsPending, productsReady, product, children,
}: {
  categories: Category[];
  categoriesPending: boolean;
  productsPending: boolean;
  productsReady: boolean;
  product?: ProductSummary;
  children: ReactNode;
}) {
  const { t } = useTranslation(["discovery", "catalog", "navigation"]);
  const store = useStore();
  const { hash } = useLocation();
  const [presented, setPresented] = useState(false);
  const [browse, setBrowse] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const focusedAction = useRef(false);
  const waiting = !presented && (productsPending || (productsReady && categoriesPending && !browse));
  // A retry must not hide an already usable homepage.
  if (!waiting && !presented) setPresented(true);

  const focusCatalog = useCallback((respectFocus = false) => {
    // Follow the in-page link after the catalog and route focus have settled.
    requestAnimationFrame(() => {
      const container = root.current;
      if (!container) return;
      const active = document.activeElement;
      if (respectFocus && active !== document.body && !container.contains(active)) return;
      const heading = container.querySelector<HTMLElement>("#shop-products");
      heading?.focus({ preventScroll: true });
      heading?.scrollIntoView?.({ block: "start" });
    });
  }, []);

  useLayoutEffect(() => {
    if (waiting) return;
    if (hash === "#shop-products") {
      focusCatalog(true);
    } else if (focusedAction.current) {
      root.current?.querySelector<HTMLAnchorElement>(".discovery-action")?.focus({ preventScroll: true });
    }
    focusedAction.current = false;
  }, [waiting, hash, focusCatalog]);

  function explore() {
    setBrowse(true);
    focusCatalog();
  }

  return (
    <div ref={root}>
      {waiting ? (
        <section className="discovery-loading" aria-busy="true" aria-labelledby="store-welcome">
          {product?.imageUrl && <link rel="preload" as="image" href={product.imageUrl} fetchPriority="high" />}
          <h1 id="store-welcome" lang={store.culture}>{store.name}</h1>
          <LoadingState label={t(productsPending ? "catalog:loadingProducts" : "navigation:categoriesLoading")} lines={2} />
          {productsReady && (
            <a className="discovery-action" href="#shop-products" onClick={explore}
              onFocus={() => { focusedAction.current = true; }} onBlur={() => { focusedAction.current = false; }}>
              {t("discovery:exploreProducts")} <span aria-hidden="true">↗</span>
            </a>
          )}
        </section>
      ) : <HomeDiscovery categories={categories} product={product} />}
      <div hidden={waiting}>{children}</div>
    </div>
  );
}
