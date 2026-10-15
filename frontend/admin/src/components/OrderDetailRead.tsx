import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api, type AdminAddress, type AdminOrderDetail } from "../api";
import { currencyFormatter, formatDateTime, formatNumber } from "../utils/format";
import { safeTrackingUrl } from "./orderDetailFacts";
import { DownloadButton } from "./ui/DownloadButton";

const attemptStates = ["Started", "Pending", "Authorized", "Unrecognised", "Paid", "Failed"] as const;
const unappliedStates = ["Open", "Refunding", "Refunded", "ClosedByHand"] as const;
const unappliedReasons = ["OrderCancelled", "OrderAlreadyPaid"] as const;
const returnStates = ["Requested", "Accepted", "Refused", "Received"] as const;
const refundStates = ["Pending", "Refunded", "Failed", "ByStore", "ClosedByHand"] as const;
function known<T extends string>(value: string, values: readonly T[]): T | "unknown" { return values.find((candidate) => candidate === value) ?? "unknown"; }
function Fact({ label, children }: { label: string; children: ReactNode }) { return <div><dt>{label}</dt><dd>{children}</dd></div>; }

export function OrderPaymentRecord({ order }: { order: AdminOrderDetail }) {
  const { t, i18n } = useTranslation("orderDetails");
  return <section className="order-panel" aria-labelledby="order-payment-title">
      <h2 id="order-payment-title" tabIndex={-1}>{t("paymentTitle")}</h2>
      <p className="hint">{t("paymentHint")}</p>
      <dl className="order-facts">
        <Fact label={t("paymentMethod")}>{order.paymentMethod}</Fact>
        <Fact label={t("paymentReference")}>{order.paymentReference ?? t("notRecorded")}</Fact>
        <Fact label={t("paidAt")}>{order.paidAt ? formatDateTime(order.paidAt, i18n.resolvedLanguage) : t("notRecorded")}</Fact>
      </dl>
    </section>;
}

export function OrderPaymentAttempts({ order }: { order: AdminOrderDetail }) {
  const { t, i18n } = useTranslation("orderDetails");
  return <section className="order-panel" aria-labelledby="order-payment-attempts">
      <h2 id="order-payment-attempts" tabIndex={-1}>{t("attemptsTitle")}</h2>
      <p className="hint">{t("attemptsHint")}</p>
      {order.paymentAttempts.length === 0 ? <p>{t("noAttempts")}</p> : <ol className="order-records">
        {order.paymentAttempts.map((attempt, index) => {
          const state = known(attempt.status, attemptStates);
          const environment = attempt.environment === null ? null : known(attempt.environment.toLowerCase(), ["test", "live"] as const);
          return <li key={`${index}:${attempt.provider}:${attempt.startedAt}`} className="order-record">
            <div className="order-record-heading"><h3>{attempt.provider}</h3><div className="cluster">
              <span className="badge">{t(`attemptStatus.${state}`)}</span>
              <span className="badge order-environment" data-environment={environment}>{environment === null ? t("environmentNotRecorded") : t(`environment.${environment}`)}</span>
            </div></div>
            <p className="hint">{t(`attemptHint.${state}`)}</p>
            {environment === "test" && <p className="order-test-hint">{t("testHint")}</p>}
            <dl className="order-facts">
              <Fact label={t("amount")}>{currencyFormatter(attempt.currency, i18n.resolvedLanguage).format(attempt.amount)}</Fact>
              <Fact label={t("reference")}>{attempt.reference ?? t("noReference")}</Fact>
              <Fact label={t("startedAt")}>{formatDateTime(attempt.startedAt, i18n.resolvedLanguage)}</Fact>
              <Fact label={t("changedAt")}>{formatDateTime(attempt.changedAt, i18n.resolvedLanguage)}</Fact>
            </dl>
          </li>;
        })}
      </ol>}
    </section>;
}

export function OrderRelatedRecords({ order, storeId }: { order: AdminOrderDetail; storeId: string }) {
  const { t, i18n } = useTranslation("orderDetails");
  return <>
    <section className="order-panel" aria-labelledby="order-unapplied">
      <h2 id="order-unapplied" tabIndex={-1}>{t("unappliedTitle")}</h2>
      <p className="hint">{t("unappliedHint")}</p>
      {order.unappliedPayments.length === 0 ? <p>{t("noUnapplied")}</p> : <ul className="order-records">
        {order.unappliedPayments.map((payment) => <li className="order-record" key={payment.id}>
          <div className="order-record-heading"><h3 id={`order-unapplied-${payment.id}`} tabIndex={-1}><a href={`#order-unapplied-${payment.id}`}>{payment.provider} · {payment.reference ?? t("noReference")}</a></h3>
            <span className="badge">{t(`unappliedStatus.${known(payment.status, unappliedStates)}`)}</span></div>
          <dl className="order-facts">
            <Fact label={t("amount")}>{currencyFormatter(payment.currency, i18n.resolvedLanguage).format(payment.amount)}</Fact>
            <Fact label={t("reason")}>{t(`unappliedReason.${known(payment.reason, unappliedReasons)}`)}</Fact>
            <Fact label={t("receivedAt")}>{formatDateTime(payment.receivedAt, i18n.resolvedLanguage)}</Fact>
            <Fact label={t("changedAt")}>{formatDateTime(payment.changedAt, i18n.resolvedLanguage)}</Fact>
          </dl>
          {payment.status === "Refunding" && <p className="hint">{t("unappliedRefundingHint")}</p>}
          {payment.status === "ClosedByHand" && <p className="hint">{t("closedByHandHint")}</p>}
          {payment.note !== null && <p className="order-record-note">{t("note")}: {payment.note}</p>}
        </li>)}
      </ul>}
    </section>
    <section className="order-panel" aria-labelledby="order-returns">
      <div className="order-record-heading"><h2 id="order-returns" tabIndex={-1}>{t("returnsTitle")}</h2><Link to={`/stores/${storeId}/returns`}>{t("storeReturns")}</Link></div>
      <p className="hint">{t("returnsHint")}</p>
      {order.returns.length === 0 ? <p>{t("noReturns")}</p> : <ul className="order-records">
        {order.returns.map((returned) => <li className="order-record" key={returned.id}>
          <div className="order-record-heading"><h3 id={`order-return-${returned.id}`} tabIndex={-1}><a href={`#order-return-${returned.id}`}>{returned.number}</a></h3>
            <span className="badge">{t(`returnStatus.${known(returned.status, returnStates)}`)}</span></div>
          <dl className="order-facts">
            <Fact label={t("creditedAmount")}>{currencyFormatter(order.currency, i18n.resolvedLanguage).format(returned.refundedAmount)}</Fact>
            <Fact label={t("refundStatusTitle")}>{returned.refund === null ? t("notRecorded") : t(`refundStatus.${known(returned.refund, refundStates)}`)}</Fact>
          </dl>
          {returned.refund === "Pending" && <p className="hint">{t("refundPendingHint")}</p>}
          {returned.refund === "ByStore" && <p className="hint">{t("refundByStoreHint")}</p>}
          {returned.refund === "ClosedByHand" && <p className="hint">{t("closedByHandHint")}</p>}
          {returned.refundProblem !== null && <p className="order-record-note">{t("refundProblem")}: {returned.refundProblem}</p>}
        </li>)}
      </ul>}
    </section>
  </>;
}

export function OrderItems({ order }: { order: AdminOrderDetail }) {
  const { t, i18n } = useTranslation(["orderDetails", "orders", "documents"]);
  const money = currencyFormatter(order.currency, i18n.resolvedLanguage);
  return <section className="order-panel" aria-labelledby="order-items-title">
      <h2 id="order-items-title">{t("itemsTitle")}</h2><p className="hint">{t("itemsHint")}</p>
      <div className="order-lines-scroll" role="region" aria-label={t("itemsTitle")} tabIndex={0}><table>
        <thead><tr><th scope="col">{t("product")}</th><th scope="col">{t("quantity")}</th><th scope="col">{t("unitPrice")}</th><th scope="col">{t("vatRate")}</th><th scope="col">{t("lineTotal")}</th></tr></thead>
        <tbody>{order.lines.map((line, index) => <tr key={`${index}:${line.productName}`}>
          <th scope="row">{line.productName}</th><td>{formatNumber(line.quantity, i18n.resolvedLanguage)}</td><td>{money.format(line.unitPrice)}</td><td>{t("orders:vat", { rate: formatNumber(line.vatRate, i18n.resolvedLanguage) })}</td><td>{money.format(line.lineTotal)}</td>
        </tr>)}</tbody>
      </table></div>
      {order.lines.length === 0 && <p>{t("noLines")}</p>}
      <dl className="order-totals">
        <Fact label={t("itemsTotal")}>{money.format(order.itemsTotal)}</Fact>
        {order.discount && <Fact label={`${t("discount")} · ${order.discount.name} (${order.discount.code})`}>−{money.format(order.discount.amount)}</Fact>}
        <Fact label={t("shippingPrice")}>{money.format(order.shippingPrice)}</Fact>
        <Fact label={t("vatIncluded")}>{money.format(order.vatTotal)}</Fact>
        <Fact label={t("orders:total")}>{money.format(order.grandTotal)}</Fact>
      </dl>
    </section>;
}

export function OrderDelivery({ order, storeId }: { order: AdminOrderDetail; storeId: string }) {
  const { t, i18n } = useTranslation(["orderDetails", "orders", "documents"]);
  const tracking = safeTrackingUrl(order.shipment?.trackingUrl ?? null);
  return <><div className="order-delivery-grid">
      <section className="order-panel" aria-labelledby="order-delivery-title">
        <h2 id="order-delivery-title">{t("deliveryTitle")}</h2>
        <dl className="order-facts">
          <Fact label={t("shippingMethod")}>{order.shippingMethod}</Fact>
          <Fact label={t("carrier")}>{order.carrier ?? t("notRecorded")}</Fact>
          <Fact label={t("phone")}>{order.phone ?? t("notRecorded")}</Fact>
          <Fact label={t("destination")}>{order.pickupPoint ?? t("deliveryAddress")}</Fact>
        </dl>
        {order.shipment ? <>
          <h3>{t("shipmentTitle")}</h3><dl className="order-facts">
            <Fact label={t("carrier")}>{order.shipment.carrier}</Fact>
            <Fact label={t("trackingNumber")}>{order.shipment.trackingNumber || t("notRecorded")}</Fact>
            <Fact label={t("shippedAt")}>{formatDateTime(order.shipment.shippedAt, i18n.resolvedLanguage)}</Fact>
          </dl>{tracking && <a href={tracking} target="_blank" rel="noopener noreferrer">{t("trackShipment")}</a>}
        </> : <p className="hint">{t("noShipment")}</p>}
      </section>
      <section className="order-panel" aria-labelledby="order-addresses-title"><h2 id="order-addresses-title">{t("addressesTitle")}</h2>
        <div className="order-addresses"><AddressBlock title={t("orders:billing")} address={order.billingAddress} /><AddressBlock title={t("orders:shipping")} address={order.shippingAddress} /></div>
      </section>
    </div>
    <section className="order-panel" aria-labelledby="order-documents-title"><h2 id="order-documents-title">{t("documentsTitle")}</h2>
      {order.documents.length === 0 ? <p>{t("noDocuments")}</p> : <ul className="order-document-list">
        {order.documents.map((document) => <li key={document.number}>
          <DownloadButton download={() => api.downloadDocument(storeId, order.number, document.number)}>{t(`documents:${document.kind === "Invoice" || document.kind === "CreditNote" ? document.kind : "Document"}`)} {document.number}</DownloadButton>
          <time dateTime={document.issuedAt}>{formatDateTime(document.issuedAt, i18n.resolvedLanguage)}</time>
        </li>)}
      </ul>}
    </section>
  </>;
}

function AddressBlock({ title, address }: { title: string; address: AdminAddress }) {
  return <div><h3>{title}</h3><address>{address.fullName}<br />{address.line1}<br />{address.line2 && <>{address.line2}<br /></>}{address.postalCode} {address.city}<br />{address.country}</address></div>;
}
