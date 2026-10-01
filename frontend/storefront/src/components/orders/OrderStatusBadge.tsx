import { useTranslation } from "react-i18next";
import { knownOrderStatus, type KnownOrderStatus } from "./orderPresentation";

const labelKeys = {
  AwaitingPayment: "status.AwaitingPayment",
  Paid: "status.Paid",
  Shipped: "status.Shipped",
  Cancelled: "status.Cancelled",
  Refunded: "status.Refunded",
  unknown: "status.unknown",
} as const satisfies Record<KnownOrderStatus, string>;

export function OrderStatusBadge({ value }: { value: string }) {
  const { t } = useTranslation("orders");
  const status = knownOrderStatus(value);

  return (
    <strong className={"order-status " + status.toLowerCase()}>
      {t(labelKeys[status])}
    </strong>
  );
}
