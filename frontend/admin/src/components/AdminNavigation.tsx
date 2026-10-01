import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { NavLink, useNavigate } from "react-router";
import type { AdminStore } from "../api";
import type { AdminOutletContext } from "../adminContext";
import { canManageStore } from "../session";

type NavigationProps = AdminOutletContext & {
  selectedStore: AdminStore | null;
  role: string;
  onNavigate?: () => void;
};

export function AdminNavigation({
  stores,
  reloadStores,
  selectedStore,
  role,
  onNavigate,
}: NavigationProps) {
  const { t } = useTranslation(["navigation", "stores", "common"]);
  const navigate = useNavigate();
  const allStores = stores.status === "ready" ? stores.data : [];

  return (
    <nav
      className="admin-navigation"
      aria-label={t("navigation:administration")}
    >
      <p className="navigation-heading">{t("navigation:company")}</p>
      <NavLink to="/products" onClick={onNavigate}>
        {t("navigation:products")}
      </NavLink>
      <NavLink to="/stock" end onClick={onNavigate}>
        {t("navigation:stock")}
      </NavLink>
      <NavLink to="/stores" end onClick={onNavigate}>
        {t("navigation:stores")}
      </NavLink>
      {canManageStore(role) && (
        <NavLink to="/stores/new" onClick={onNavigate}>
          {t("navigation:newStore")}
        </NavLink>
      )}

      <div className="store-selector">
        {stores.status === "loading" && (
          <span className="navigation-status" role="status">
            {t("stores:loading")}
          </span>
        )}
        {(stores.status === "error" || stores.status === "not-found") && (
          <span className="navigation-status" role="alert">
            {t("stores:loadFailed")}
            <button
              type="button"
              className="link-button compact-link"
              onClick={reloadStores}
            >
              {t("common:retry")}
            </button>
          </span>
        )}
        {stores.status === "ready" && allStores.length === 0 && (
          <span className="navigation-status">{t("stores:none")}</span>
        )}
        {stores.status === "ready" && allStores.length > 0 && (
          <label>
            {t("stores:selectStore")}
            <select
              value={selectedStore?.id ?? ""}
              onChange={(event) => {
                if (!event.target.value) return;
                onNavigate?.();
                void navigate(`/stores/${event.target.value}`);
              }}
            >
              <option value="">{t("stores:chooseStore")}</option>
              {allStores.map((store) => (
                <option key={store.id} value={store.id}>
                  {store.name}
                  {store.status === "draft" ? ` · ${t("common:draft")}` : ""}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>

      {selectedStore && (
        <>
          <p
            className="navigation-heading store-navigation-name"
            lang={selectedStore.culture}
          >
            {selectedStore.name}
          </p>
          <NavLink to={`/stores/${selectedStore.id}`} end onClick={onNavigate}>
            {t("navigation:overview")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/settings`}
            onClick={onNavigate}
          >
            {t("navigation:settings")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/products`}
            onClick={onNavigate}
          >
            {t("navigation:storeProducts")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/categories`}
            onClick={onNavigate}
          >
            {t("navigation:categories")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/attributes`}
            onClick={onNavigate}
          >
            {t("navigation:attributes")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/import`}
            onClick={onNavigate}
          >
            {t("navigation:import")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/methods`}
            onClick={onNavigate}
          >
            {t("navigation:methods")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/discounts`}
            onClick={onNavigate}
          >
            {t("navigation:discounts")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/orders`}
            onClick={onNavigate}
          >
            {t("navigation:orders")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/returns`}
            onClick={onNavigate}
          >
            {t("navigation:returns")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/reviews`}
            onClick={onNavigate}
          >
            {t("navigation:reviews")}
          </NavLink>
          <NavLink
            to={`/stores/${selectedStore.id}/operations`}
            onClick={onNavigate}
          >
            {t("navigation:operations")}
          </NavLink>
        </>
      )}
    </nav>
  );
}

export function MobileAdminNavigation(props: NavigationProps) {
  const { t } = useTranslation("navigation");
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    panel.current?.querySelector<HTMLElement>("a, button, select")?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setOpen(false);
      trigger.current?.focus();
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open]);

  return (
    <div className="mobile-admin-navigation">
      <button
        ref={trigger}
        type="button"
        className="admin-menu-trigger"
        aria-expanded={open}
        aria-controls="admin-mobile-menu"
        onClick={() => setOpen((current) => !current)}
      >
        {open ? t("closeMenu") : t("menu")}
      </button>
      {open && (
        <div ref={panel} id="admin-mobile-menu" className="admin-mobile-panel">
          <AdminNavigation {...props} onNavigate={() => setOpen(false)} />
        </div>
      )}
    </div>
  );
}
