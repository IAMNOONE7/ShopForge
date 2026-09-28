import { useTranslation } from "react-i18next";
import { Link, Outlet, useParams } from "react-router";
import { useAdminOutlet, type StoreOutletContext } from "../adminContext";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";

export function StoreLayout() {
  const { t } = useTranslation(["stores", "navigation"]);
  const { storeId = "" } = useParams();
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

  const store = stores.data.find((candidate) => candidate.id === storeId);
  if (!store)
    return (
      <EmptyState
        title={t("stores:notFoundTitle")}
        action={<Link to="/stores">{t("navigation:stores")}</Link>}
      >
        {t("stores:notFoundBody")}
      </EmptyState>
    );

  const context: StoreOutletContext = { store, reloadStores };
  return <Outlet key={store.id} context={context} />;
}
