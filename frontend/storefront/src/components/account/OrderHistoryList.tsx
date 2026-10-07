import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { CustomerOrder } from "../../account";
import { formatDate } from "../../utils/format";
import { OrderStatusBadge } from "../orders/OrderStatusBadge";

export function OrderHistoryList({ orders, culture }: { orders: CustomerOrder[]; culture: string }) {
  const { t } = useTranslation(["account", "orders"]);
  return (
    <ul className="account-order-list" aria-label={t("account:orders")}>
      {orders.map((order) => (
        <li key={order.number}>
          <div className="account-order-heading">
            <Link to={`/account/orders/${encodeURIComponent(order.number)}`} aria-label={t("account:viewOrder", { number: order.number })}>
              <bdi>{order.number}</bdi>
              <span>{t("account:orderDetail")} <span aria-hidden="true">→</span></span>
            </Link>
            <OrderStatusBadge value={order.status} />
          </div>
          <dl>
            <div><dt>{t("account:orderDate")}</dt><dd><time dateTime={order.placedAt}>{formatDate(order.placedAt, culture)}</time></dd></div>
            <div><dt>{t("account:orderItems")}</dt><dd>{t("orders:itemCount", { count: order.items })}</dd></div>
          </dl>
        </li>
      ))}
    </ul>
  );
}
