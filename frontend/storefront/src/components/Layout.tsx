import { useTranslation } from "react-i18next";
import { Link, NavLink, Outlet, useLocation } from "react-router";
import { getCategories, type Category } from "../api";
import { useCart } from "../cartContext";
import { useCustomer } from "../customerContext";
import { useStore } from "../storeContext";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
import { RouteEffects } from "./RouteEffects";
import { RouteContent } from "./RouteContent";
import { StoreBrand } from "./StoreBrand";
import { categoryTree } from "./categoryTree";
import { categoryPath } from "../publicPages";
import {
  CategoryNavigation,
  MobileNavigation,
  type CategoryState,
} from "./StorefrontNavigation";

export function Layout() {
  const { t } = useTranslation("navigation");
  const store = useStore();
  const location = useLocation();
  const { status: cartStatus, cart } = useCart();
  const { status: customerStatus, customer } = useCustomer();
  const categories = useRequest("categories", getCategories);
  const categoryList: Category[] =
    categories.status === "ready" ? categories.data : [];
  const categoryState: CategoryState =
    categories.status === "loading"
      ? { status: "loading" }
      : categories.status === "error" || categories.status === "not-found"
        ? { status: "error", retry: categories.reload }
        : { status: "ready", categories: categories.data };
  const accountTo =
    customerStatus === "guest"
      ? "/account/sign-in?returnTo=%2Faccount"
      : "/account";
  const accountLabel = customer
    ? customer.firstName
    : customerStatus === "guest"
      ? t("signIn")
      : t("account");

  return (
    <div className="storefront-frame">
      <RouteEffects store={store} categories={categoryList} />
      <a href="#main-content" className="skip-link">
        {t("skipToContent")}
      </a>
      <header className="store-header">
        <div className="store-header-inner container">
          <StoreBrand />
          <NavLink to={accountTo} className="account-link">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
              <circle cx="12" cy="8" r="3.5" />
              <path d="M4.5 21v-2a7.5 7.5 0 0 1 15 0v2" />
            </svg>
            {accountLabel}
          </NavLink>
          <NavLink to="/cart" className="cart-link">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
              <path d="M4 7h16l-1 14H5L4 7Z" />
              <path d="M8 8V6a4 4 0 0 1 8 0v2" />
            </svg>
            {t("cart")}
            {cartStatus === "ready" && cart && cart.count > 0 && (
              <span className="cart-count">{cart.count}</span>
            )}
          </NavLink>
          <MobileNavigation
            key={location.pathname}
            state={categoryState}
            contentLanguage={store.culture}
          />
        </div>
        <div className="store-category-bar desktop-navigation">
          <CategoryNavigation
            state={categoryState}
            className="store-nav container"
            variant="desktop"
            contentLanguage={store.culture}
          />
        </div>
      </header>
      <main id="main-content" className="app container" tabIndex={-1}>
        {categories.status === "error" && (
          <div className="shell-request-error">
            <RequestError
              error={categories.error}
              operation="read"
              onRetry={categories.reload}
            />
          </div>
        )}
        {categories.status === "ready" && categories.refreshError !== null && (
          <div className="shell-request-error">
            <RequestError
              error={categories.refreshError}
              operation="read"
              onRetry={categories.reload}
            />
          </div>
        )}
        <RouteContent><Outlet context={categoryList} /></RouteContent>
      </main>
      <footer className="store-footer">
        <div className="store-footer-inner container">
          <div className="footer-identity">
            <Link to="/" className="footer-brand" lang={store.culture}>
              {store.name}
            </Link>
            <a href="#main-content" className="footer-top">{t("backToTop")} <span aria-hidden="true">↑</span></a>
          </div>
          {categoryList.length > 0 && (
            <nav className="footer-links" aria-label={t("footerCategories")}>
              <h2>{t("categories")}</h2>
              {categoryTree(categoryList).map(({ category }) => (
                <Link key={category.slug} to={categoryPath(category.slug)} lang={store.culture}>{category.name}</Link>
              ))}
            </nav>
          )}
          <nav className="footer-links" aria-label={t("footerNavigation")}>
            <h2>{t("shopping")}</h2>
            <Link to="/">{t("allProducts")}</Link>
            <Link to={accountTo}>{accountLabel}</Link>
            <Link to="/cart">{t("cart")}</Link>
          </nav>
        </div>
      </footer>
    </div>
  );
}
