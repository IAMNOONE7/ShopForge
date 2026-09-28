import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useAdminOutlet } from "../adminContext";
import { canManageStore, useSession } from "../session";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";

export function StoreIndexPage() {
  const { t } = useTranslation(["stores", "navigation", "common"]);
  const { user } = useSession();
  const { stores, reloadStores } = useAdminOutlet();

  if (stores.status === "loading")
    return <LoadingState label={t("stores:loading")} lines={4} />;
  if (stores.status === "error" || stores.status === "not-found")
    return (
      <RequestError
        error={stores.error}
        operation="read"
        onRetry={reloadStores}
      />
    );
  if (stores.data.length === 0)
    return (
      <EmptyState
        title={t("stores:emptyTitle")}
        action={
          canManageStore(user.role) ? (
            <Link to="/stores/new" className="button">
              {t("navigation:newStore")}
            </Link>
          ) : undefined
        }
      >
        {t("stores:emptyBody")}
      </EmptyState>
    );

  return (
    <>
      <div className="page-heading-row">
        <h1>{t("stores:title")}</h1>
        {canManageStore(user.role) && (
          <Link to="/stores/new" className="button">
            {t("navigation:newStore")}
          </Link>
        )}
      </div>
      <ul className="store-list">
        {stores.data.map((store) => (
          <li key={store.id}>
            <Link to={`/stores/${store.id}`} lang={store.culture}>
              <strong>{store.name}</strong>
              <span>
                {store.primaryHostName ?? t("stores:noAddress")} ·{" "}
                {store.currency}
              </span>
              {store.status === "draft" && (
                <span className="badge">{t("common:draft")}</span>
              )}
            </Link>
          </li>
        ))}
      </ul>
    </>
  );
}
