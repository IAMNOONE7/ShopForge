import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { getOrders, type CustomerOrder } from "../../account";
import { useStore } from "../../storeContext";
import { useRequest } from "../../useRequest";
import { formatDate } from "../../utils/format";
import { OrderStatusBadge } from "../orders/OrderStatusBadge";
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
          <h2 id="account-orders-heading">{t("account:orders")}</h2>
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
        <OrderHistoryTable orders={orders.data} culture={store.culture} />
      )}
    </section>
  );
}

function OrderHistoryTable({
  orders,
  culture,
}: {
  orders: CustomerOrder[];
  culture: string;
}) {
  const { t } = useTranslation(["account", "orders"]);

  return (
    <div className="account-orders-wrap">
      <table className="account-orders-table">
        <thead>
          <tr>
            <th scope="col">{t("account:orderNumber")}</th>
            <th scope="col">{t("account:orderDate")}</th>
            <th scope="col">{t("account:orderStatus")}</th>
            <th scope="col">{t("account:orderItems")}</th>
            <th scope="col">
              <span className="sr-only">{t("account:orderDetail")}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {orders.map((order) => {
            const path = `/account/orders/${encodeURIComponent(order.number)}`;
            return (
              <tr key={order.number}>
                <th scope="row" data-label={t("account:orderNumber")}>
                  <Link
                    to={path}
                    aria-label={t("account:viewOrder", {
                      number: order.number,
                    })}
                  >
                    {order.number}
                  </Link>
                </th>
                <td data-label={t("account:orderDate")}>
                  <time dateTime={order.placedAt}>
                    {formatDate(order.placedAt, culture)}
                  </time>
                </td>
                <td data-label={t("account:orderStatus")}>
                  <OrderStatusBadge value={order.status} />
                </td>
                <td data-label={t("account:orderItems")}>
                  {t("orders:itemCount", { count: order.items })}
                </td>
                <td
                  className="account-order-action"
                  data-label={t("account:orderDetail")}
                >
                  <Link to={path}>{t("account:orderDetail")}</Link>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
