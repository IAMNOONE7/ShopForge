import { useTranslation } from "react-i18next";

export function OrderStatus({ status }: { status: string }) {
  const { t } = useTranslation("orders");
  const known = ["AwaitingPayment", "Paid", "Shipped", "Refunded", "Cancelled"].includes(status)
    ? status as "AwaitingPayment" | "Paid" | "Shipped" | "Refunded" | "Cancelled" : "unknown";
  return <span className="badge">{t(`status.${known}`)}</span>;
}
