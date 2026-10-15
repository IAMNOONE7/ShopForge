import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type AdminAddress, type AdminOrderDetail } from "../api";
import { HttpError } from "../api/http";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { currencyFormatter, formatDateTime } from "../utils/format";
import { OrderStatus } from "./OrderStatus";
import { Button } from "./ui/Button";
import { DownloadButton } from "./ui/DownloadButton";
import { EmptyState } from "./ui/EmptyState";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

type Props = { storeId: string; number: string; canManage: boolean };

export function OrderDetailsSection({ storeId, number, canManage }: Props) {
  const { t, i18n } = useTranslation("orders");
  const [detail, reload] = useRequest(`order:${storeId}:${number}`, async (signal) => {
    const order = await api.order(storeId, number, signal);
    if (order.number !== number) throw new HttpError("invalid-response", 200);
    return { order };
  });
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const [staleSnapshot, setStaleSnapshot] = useState<{ order: AdminOrderDetail } | null>(null);
  const [tracking, setTracking] = useState("");
  const mounted = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const ready = detail.status === "ready" ? detail : null;
  const order = ready?.data.order;
  const disabled = pending || !ready || ready.refreshing || ready.refreshError !== null || ready.data === staleSnapshot;
  // An incomplete payment history cannot establish that manual confirmation is safe.
  const gatewayOpen = !Array.isArray(order?.paymentAttempts) || order.paymentAttempts.some((attempt) => attempt.reference !== null && !["Paid", "Failed"].includes(attempt.status));

  async function change(action: () => Promise<unknown>) {
    if (disabled || !canManage) return;
    await run(async () => {
      try { await action(); }
      finally {
        if (mounted.current && ready) { setStaleSnapshot(ready.data); reload(); }
      }
    });
  }
  function ship(event: FormEvent<HTMLFormElement>) { event.preventDefault(); void change(() => api.createShipment(storeId, number, tracking.trim())); }

  return <>
    <div className="order-page-heading"><h1>{t("detailTitle", { number })}</h1>
      <Button type="button" variant="secondary" disabled={pending || detail.status === "loading" || ready?.refreshing} onClick={reload}>{t("refresh")}</Button></div>
    {detail.status === "loading" && <LoadingState label={t("loading")} />}
    {detail.status === "not-found" && <EmptyState headingLevel={2} title={t("missingTitle")} action={<Button type="button" variant="secondary" onClick={reload}>{t("refresh")}</Button>}><p>{t("missingBody")}</p></EmptyState>}
    {detail.status === "error" && <RequestError error={detail.error} operation="read" onRetry={reload} />}
    {ready && order && <>
      {ready.refreshing && <p role="status">{t("refreshingDetail")}</p>}
      {ready.refreshError !== null && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
      {(ready.refreshing || ready.refreshError !== null || ready.data === staleSnapshot) && <p className="hint">{t("staleDetail")}</p>}
      <div className="order-detail-summary"><OrderStatus status={order.status} /><p>{order.email}</p><p><time dateTime={order.placedAt}>{formatDateTime(order.placedAt, i18n.resolvedLanguage)}</time></p></div>
      {canManage && <section className="order-actions" aria-label={t("actions")}>
        {pending && <p role="status">{t("saving")}</p>}
        {error !== null && <RequestError error={error} operation="write" />}
        <div className="cluster">
          {order.status === "AwaitingPayment" && <>
            {!gatewayOpen && <Button type="button" disabled={disabled} onClick={() => void change(() => api.confirmOrderPayment(storeId, number))}>{t("markPaid")}</Button>}
            <Button type="button" variant="secondary" disabled={disabled} onClick={() => void change(() => api.cancelOrder(storeId, number))}>{t("cancel")}</Button>
          </>}
          {["Paid", "Shipped"].includes(order.status) && <Button type="button" variant="secondary" disabled={disabled} onClick={() => void change(() => api.refundOrder(storeId, number))}>{t("refund")}</Button>}
        </div>
        {order.status === "AwaitingPayment" && gatewayOpen && <p className="hint">{t("gatewayManaged")}</p>}
        {order.status === "Paid" && <form className="order-shipment-form" onSubmit={ship}>
          <label htmlFor="order-tracking">{t("trackingNumber")}</label>
          <input id="order-tracking" name="trackingNumber" value={tracking} required disabled={disabled} onChange={(event) => setTracking(event.target.value)} />
          <Button type="submit" disabled={disabled}>{t("ship")}</Button>
        </form>}
      </section>}
      <OrderDetails storeId={storeId} order={order} />
    </>}
  </>;
}

function OrderDetails({ storeId, order }: { storeId: string; order: AdminOrderDetail }) {
  const { t, i18n } = useTranslation(["orders", "documents"]);
  const money = currencyFormatter(order.currency, i18n.resolvedLanguage);
  return (
    <div className="order-detail">
      <div className="order-lines-scroll" role="region" aria-label={t("orders:items")} tabIndex={0}><table>
        <tbody>
          {order.lines.map((line, index) => (
            <tr key={`${index}:${line.productName}`}>
              <td>{line.productName}</td>
              <td>{line.quantity} ×</td>
              <td>{money.format(line.unitPrice)}</td>
              <td>{t("orders:vat", { rate: line.vatRate })}</td>
              <td>{money.format(line.lineTotal)}</td>
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
      </table></div>
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
        {t("orders:carriedBy", {
          carrier: order.carrier ?? t("orders:carrierUnknown"),
        })}{" "}
        {order.phone && t("orders:phone", { phone: order.phone })}{" "}
        {order.pickupPoint
          ? t("orders:pickup", { point: order.pickupPoint })
          : t("orders:toTheAddress")}{" "}
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
