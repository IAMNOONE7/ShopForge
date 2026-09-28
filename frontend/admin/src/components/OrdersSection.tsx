import { useState } from "react";
import { useTranslation } from "react-i18next";
import { api, type AdminAddress, type AdminOrder } from "../api";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
import { DownloadButton } from "./ui/DownloadButton";
import {
  currencyFormatter,
  formatDateTime,
  formatNumber,
} from "../utils/format";
type Props = { storeId: string; canManage: boolean };
export function OrdersSection({ storeId, canManage }: Props) {
  const { t } = useTranslation(["orders", "errors"]);
  const [orders, reload] = useRequest(`orders:${storeId}`, (signal) =>
    api.orders(storeId, signal),
  );
  const [error, run] = useAction(reload);
  const [open, setOpen] = useState<string | null>(null);
  const list: AdminOrder[] = orders.status === "ready" ? orders.data : [];
  return (
    <section>
      <h2>{t("orders:title")}</h2>
      <p className="hint">{t("orders:hint")}</p>
      {error !== null && <RequestError error={error} operation="write" />}
      {orders.status === "error" && (
        <RequestError error={orders.error} operation="read" onRetry={reload} />
      )}
      {orders.status === "ready" && orders.refreshError !== null && (
        <RequestError
          error={orders.refreshError}
          operation="read"
          onRetry={reload}
        />
      )}
      {orders.status === "ready" && list.length === 0 && (
        <p className="hint">{t("orders:empty")}</p>
      )}
      {list.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>{t("orders:number")}</th>
              <th>{t("orders:placed")}</th>
              <th>{t("orders:customer")}</th>
              <th>{t("orders:statusHeading")}</th>
              <th>{t("orders:items")}</th>
              <th>{t("orders:total")}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {list.map((order) => (
              <Rows
                key={order.number}
                storeId={storeId}
                order={order}
                isOpen={open === order.number}
                onToggle={() =>
                  setOpen(open === order.number ? null : order.number)
                }
                run={run}
                canManage={canManage}
              />
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
type RowsProps = {
  storeId: string;
  order: AdminOrder;
  isOpen: boolean;
  onToggle: () => void;
  run: (change: () => Promise<unknown>) => Promise<void>;
  canManage: boolean;
};
function Rows({ storeId, order, isOpen, onToggle, run, canManage }: RowsProps) {
  const { t, i18n } = useTranslation(["orders", "common"]);
  const awaitingPayment = order.status === "AwaitingPayment";
  const paid = order.status === "Paid";
  const refundable = order.status === "Paid" || order.status === "Shipped";
  return (
    <>
      <tr>
        <td>{order.number}</td>
        <td>{formatDateTime(order.placedAt, i18n.resolvedLanguage)}</td>
        <td>
          {order.email}
          {!order.hasAccount && (
            <span className="hint"> ({t("orders:guest")})</span>
          )}
        </td>
        <td>{t(`orders:status.${knownStatus(order.status)}`)}</td>
        <td>{order.items}</td>
        <td>{formatNumber(order.grandTotal, i18n.resolvedLanguage)}</td>
        <td className="inline-form compact">
          <button type="button" onClick={onToggle}>
            {isOpen ? t("common:hide") : t("common:details")}
          </button>
          {canManage && awaitingPayment && (
            <>
              <button
                type="button"
                onClick={() =>
                  run(() => api.confirmOrderPayment(storeId, order.number))
                }
              >
                {t("orders:markPaid")}
              </button>
              <button
                type="button"
                onClick={() =>
                  run(() => api.cancelOrder(storeId, order.number))
                }
              >
                {t("orders:cancel")}
              </button>
            </>
          )}
          {canManage && refundable && (
            <button
              type="button"
              onClick={() => run(() => api.refundOrder(storeId, order.number))}
            >
              {t("orders:refund")}
            </button>
          )}
          {canManage && paid && (
            <form
              className="inline-form compact"
              action={(form) =>
                run(() =>
                  api.createShipment(
                    storeId,
                    order.number,
                    String(form.get("trackingNumber")),
                  ),
                )
              }
            >
              <input
                name="trackingNumber"
                placeholder={t("orders:trackingNumber")}
                required
              />
              <button type="submit">{t("orders:ship")}</button>
            </form>
          )}
        </td>
      </tr>
      {isOpen && (
        <tr>
          <td colSpan={7}>
            <OrderDetail storeId={storeId} number={order.number} />
          </td>
        </tr>
      )}
    </>
  );
}
function knownStatus(
  status: string,
):
  | "AwaitingPayment"
  | "Paid"
  | "Shipped"
  | "Refunded"
  | "Cancelled"
  | "unknown" {
  return [
    "AwaitingPayment",
    "Paid",
    "Shipped",
    "Refunded",
    "Cancelled",
  ].includes(status)
    ? (status as
        | "AwaitingPayment"
        | "Paid"
        | "Shipped"
        | "Refunded"
        | "Cancelled")
    : "unknown";
}
function OrderDetail({ storeId, number }: { storeId: string; number: string }) {
  const { t, i18n } = useTranslation(["orders", "documents", "errors"]);
  const [detail] = useRequest(`order:${storeId}:${number}`, (signal) =>
    api.order(storeId, number, signal),
  );
  if (detail.status === "error")
    return <RequestError error={detail.error} operation="read" />;
  if (detail.status !== "ready")
    return <p className="hint">{t("orders:loading")}</p>;
  const order = detail.data;
  const money = currencyFormatter(order.currency, i18n.resolvedLanguage);
  return (
    <div className="order-detail">
      <table>
        <tbody>
          {order.lines.map((line) => (
            <tr key={line.productName}>
              <td>{line.productName}</td>
              <td>{line.quantity} ×</td>
              <td>{money.format(line.unitPrice)}</td>
              <td>{t("orders:vat", { rate: line.vatRate })}</td>
              <td>{money.format(line.unitPrice * line.quantity)}</td>
            </tr>
          ))}
          {order.discount && (
            <tr>
              <td colSpan={4}>
                {order.discount.name} ({order.discount.code})
              </td>
              <td>−{money.format(order.discount.amount)}</td>
            </tr>
          )}
          <tr>
            <td colSpan={4}>{order.shippingMethod}</td>
            <td>{money.format(order.shippingPrice)}</td>
          </tr>
          <tr>
            <td colSpan={4}>
              {t("orders:totalWithVat", { vat: money.format(order.vatTotal) })}
            </td>
            <td>
              <strong>{money.format(order.grandTotal)}</strong>
            </td>
          </tr>
        </tbody>
      </table>
      {order.documents.length > 0 && (
        <p className="inline-form compact">
          {order.documents.map((document) => (
            <DownloadButton
              key={document.number}
              download={() =>
                api.downloadDocument(storeId, order.number, document.number)
              }
            >
              {t(
                `documents:${document.kind === "CreditNote" ? "CreditNote" : "Invoice"}`,
              )}{" "}
              {document.number}
            </DownloadButton>
          ))}
        </p>
      )}
      <p className="hint">
        {t("orders:payment", { method: order.paymentMethod })}{" "}
        {order.pickupPoint && t("orders:pickup", { point: order.pickupPoint })}{" "}
        {order.shipment &&
          t("orders:shipment", {
            carrier: order.shipment.carrier,
            trackingNumber: order.shipment.trackingNumber,
          })}
      </p>
      <div className="chips">
        <AddressBlock
          title={t("orders:billing")}
          address={order.billingAddress}
        />
        <AddressBlock
          title={t("orders:shipping")}
          address={order.shippingAddress}
        />
      </div>
    </div>
  );
}
function AddressBlock({
  title,
  address,
}: {
  title: string;
  address: AdminAddress;
}) {
  return (
    <address>
      <strong>{title}</strong>
      <br />
      {address.fullName}
      <br />
      {address.line1}
      <br />
      {address.line2 && (
        <>
          {address.line2}
          <br />
        </>
      )}
      {address.postalCode} {address.city}
      <br />
      {address.country}
    </address>
  );
}
