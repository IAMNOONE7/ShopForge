import { useEffect, useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";
import type { Category } from "../api";
import type { Store } from "../store";
import { searchTerm } from "../search";
import { hasPageTitle, installPageMetadata } from "../pageMetadata";

export function RouteEffects({
  store,
  categories,
}: {
  store: Store;
  categories: Category[];
}) {
  const { t, i18n } = useTranslation([
    "navigation",
    "auth",
    "account",
    "checkout",
    "orders",
    "errors",
    "catalog",
  ]);
  const location = useLocation();
  const query = location.pathname === "/" || location.pathname.startsWith("/c/")
    ? searchTerm(new URLSearchParams(location.search).get("q")) : "";
  const previousRoute = useRef<{ pathname: string; query: string } | null>(null);
  const fallbackTitle = useMemo(() => {
    const segments = location.pathname
      .split("/")
      .filter(Boolean)
      .map(decodeURIComponent);
    if (segments.length === 0) return query ? t("catalog:searchResults", { query }) : t("navigation:allProducts");
    if (segments[0] === "c") {
      return (
        categories.find((category) => category.slug === segments[1])?.name ??
        t("navigation:products")
      );
    }
    if (segments[0] === "p") return t("navigation:product");
    if (segments[0] === "cart") return t("navigation:cart");
    if (segments[0] === "checkout") return t("checkout:title");
    if (segments[0] === "order")
      return t("orders:orderTitle", { number: segments[1] ?? "" });
    if (segments[0] !== "account") return t("errors:pageNotFoundTitle");
    if (segments[1] === "orders")
      return t("orders:orderTitle", { number: segments[2] ?? "" });
    if (segments[1] === "sign-in") return t("auth:signIn");
    if (segments[1] === "register") return t("auth:createAccount");
    if (segments[1] === "forgot-password") return t("auth:forgottenPassword");
    if (segments[1] === "reset-password") return t("auth:chooseNewPassword");
    if (segments[1] === "verify") return t("auth:checkEmail");
    return t("account:title");
  }, [categories, location.pathname, query, t]);

  useEffect(() => {
    const main = document.getElementById("main-content");
    if (!main) return;
    const shouldFocus =
      previousRoute.current !== null &&
      (previousRoute.current.pathname !== location.pathname || previousRoute.current.query !== query);
    previousRoute.current = { pathname: location.pathname, query };
    let focused = false;
    let fallbackMetadata: (() => void) | undefined;

    function update() {
      if (!document.head.querySelector('meta[data-shopforge-seo][name="robots"]')) {
        fallbackMetadata?.(); fallbackMetadata = installPageMetadata(null);
      }
      const candidate = main?.querySelector<HTMLElement>("h1");
      const heading = candidate?.closest('[aria-busy="true"]') ? null : candidate;
      const page = heading?.textContent?.trim() || fallbackTitle;
      if (!hasPageTitle()) document.title = location.pathname === "/" && page === store.name
        ? store.name
        : t("navigation:pageTitle", { page, store: store.name });
      if (!shouldFocus || focused || !heading) return;
      focused = true;
      if (!heading.hasAttribute("tabindex"))
        heading.setAttribute("tabindex", "-1");
      heading.focus({ preventScroll: true });
      heading.scrollIntoView?.({ block: "start" });
    }

    update();
    const observer = new MutationObserver(update);
    observer.observe(main, {
      childList: true,
      subtree: true,
      characterData: true,
      attributes: true,
      attributeFilter: ["aria-busy"],
    });
    observer.observe(document.head, { attributes: true, attributeFilter: ["data-shopforge-title"] });
    return () => { observer.disconnect(); fallbackMetadata?.(); };
  }, [fallbackTitle, i18n.resolvedLanguage, location.pathname, query, store.name, t]);

  return null;
}
