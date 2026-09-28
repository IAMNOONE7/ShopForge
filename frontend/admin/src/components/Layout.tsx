import { useTranslation } from "react-i18next";
import { Link, Outlet, useLocation } from "react-router";
import { api } from "../api";
import type { AdminOutletContext } from "../adminContext";
import { knownRole, useSession } from "../session";
import { useRequest } from "../useRequest";
import { AdminNavigation, MobileAdminNavigation } from "./AdminNavigation";
import { AdminRouteEffects } from "./AdminRouteEffects";
import { RequestError } from "./ui/RequestError";

export function Layout() {
  const { t } = useTranslation(["navigation", "auth"]);
  const { user, logout, logoutPending, logoutError } = useSession();
  const [stores, reloadStores] = useRequest("stores", api.stores);
  const location = useLocation();
  const storeId = storeIdFromPath(location.pathname);
  const selectedStore =
    stores.status === "ready"
      ? (stores.data.find((store) => store.id === storeId) ?? null)
      : null;
  const outletContext: AdminOutletContext = { stores, reloadStores };
  const navigation = {
    stores,
    reloadStores,
    selectedStore,
    role: user.role,
  };

  return (
    <div className="admin-frame">
      <AdminRouteEffects />
      <a href="#main-content" className="skip-link">
        {t("navigation:skipToContent")}
      </a>
      <header className="admin-header">
        <div className="admin-header-inner container">
          <Link to="/products" className="admin-brand">
            {t("auth:title")}
          </Link>
          <MobileAdminNavigation key={location.pathname} {...navigation} />
          <div className="admin-user">
            <span>
              <strong>{user.email}</strong>
              <span>{t(`auth:roles.${knownRole(user.role)}`)}</span>
            </span>
            <button
              type="button"
              disabled={logoutPending}
              onClick={() => void logout()}
            >
              {t("navigation:signOut")}
            </button>
          </div>
        </div>
      </header>
      <div className="admin-shell container">
        <aside className="admin-sidebar">
          <AdminNavigation {...navigation} />
        </aside>
        <main id="main-content" className="admin-main" tabIndex={-1}>
          {logoutError !== null && (
            <RequestError error={logoutError} operation="session" />
          )}
          <Outlet context={outletContext} />
        </main>
      </div>
    </div>
  );
}

function storeIdFromPath(pathname: string) {
  const match = /^\/stores\/([^/]+)(?:\/|$)/.exec(pathname);
  if (!match || match[1] === "new") return null;
  try {
    return decodeURIComponent(match[1]);
  } catch {
    return match[1];
  }
}
