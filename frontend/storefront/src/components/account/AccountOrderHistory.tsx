import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { getOrders } from "../../account";
import { useStore } from "../../storeContext";
import { useRequest } from "../../useRequest";
import { OrderHistoryList } from "./OrderHistoryList";
import { LoadingState } from "../ui/LoadingState";
import { RequestError } from "../ui/RequestError";

export function AccountOrderHistory({
  customerKey,
}: {
  customerKey: string;
}) {
  const { t } = useTranslation(["account", "orders", "navigation"]);
  const store = useStore();
  const orders = useRequest(`account-orders:${customerKey}`, getOrders);

  return (
    <section
      className="account-panel account-history"
      aria-labelledby="account-orders-heading"
    >
      <div className="account-section-heading">
        <div>
          <h2 id="account-orders-heading" tabIndex={-1}>{t("account:orders")}</h2>
          <p>{t("account:ordersIntro")}</p>
        </div>
      </div>

      {orders.status === "loading" && (
        <LoadingState label={t("account:ordersLoading")} lines={4} />
      )}
      {(orders.status === "error" || orders.status === "not-found") && (
        <RequestError
          error={orders.error}
          operation="read"
          onRetry={orders.reload}
        />
      )}
      {orders.status === "ready" && orders.refreshError !== null && (
        <RequestError
          error={orders.refreshError}
          operation="read"
          onRetry={orders.reload}
        />
      )}
      {orders.status === "ready" && orders.data.length === 0 && (
        <div className="account-history-empty">
          <p>{t("account:noOrders")}</p>
          <Link to="/" className="button">
            {t("navigation:allProducts")}
          </Link>
        </div>
      )}
      {orders.status === "ready" && orders.data.length > 0 && (
        <OrderHistoryList orders={orders.data} culture={store.culture} />
      )}
    </section>
  );
}

