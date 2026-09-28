import { useTranslation } from "react-i18next";
import { Link, NavLink, Outlet, useLocation } from "react-router";
import { getCategories, type Category } from "../api";
import { useCart } from "../cartContext";
import { useCustomer } from "../customerContext";
import { useStore } from "../storeContext";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
import { RouteEffects } from "./RouteEffects";
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
    customerStatus === "guest" ? "/account/sign-in" : "/account";
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
          <Link to="/" className="store-brand" lang={store.culture}>
            {store.logoUrl ? (
              <img
                src={store.logoUrl}
                alt={store.name}
                className="store-logo"
              />
            ) : (
              store.name
            )}
          </Link>
          <CategoryNavigation
            state={categoryState}
            className="store-nav desktop-navigation"
            contentLanguage={store.culture}
          />
          <NavLink to={accountTo} className="account-link">
            {accountLabel}
          </NavLink>
          <NavLink to="/cart" className="cart-link">
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
        <Outlet context={categoryList} />
      </main>
      <footer className="store-footer">
        <div className="store-footer-inner container">
          <Link to="/" className="footer-brand" lang={store.culture}>
            {store.name}
          </Link>
          <nav aria-label={t("footerNavigation")}>
            <Link to="/">{t("allProducts")}</Link>
            <Link to={accountTo}>{accountLabel}</Link>
            <Link to="/cart">{t("cart")}</Link>
          </nav>
        </div>
      </footer>
    </div>
  );
}
