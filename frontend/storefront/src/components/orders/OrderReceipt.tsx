import type { ReactNode } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import type { Order, OrderDocument } from "../../cart";
import type { Store } from "../../store";
import { formatCurrency, formatDateTime } from "../../utils/format";
import { DownloadButton } from "../ui/DownloadButton";
import { RequestError } from "../ui/RequestError";
import {
  expectedDocumentKind,
  knownDocumentKind,
  knownOrderStatus,
  safeTrackingUrl,
  type KnownDocumentKind,
  type KnownOrderStatus,
} from "./orderPresentation";
import { OrderStatusBadge } from "./OrderStatusBadge";

const statusNoticeKeys = {
  AwaitingPayment: "orders:statusNotice.AwaitingPayment",
  Paid: "orders:statusNotice.Paid",
  Shipped: "orders:statusNotice.Shipped",
  Cancelled: "orders:statusNotice.Cancelled",
  Refunded: "orders:statusNotice.Refunded",
  unknown: "orders:statusNotice.unknown",
} as const satisfies Record<KnownOrderStatus, string>;

const documentKeys = {
  Invoice: "documents:Invoice",
  CreditNote: "documents:CreditNote",
  unknown: "documents:unknown",
} as const satisfies Record<KnownDocumentKind, string>;

export type OrderRefreshState = {
  waiting: boolean;
  refreshing: boolean;
  error: unknown | null;
  timedOut: boolean;
  onRefresh: () => void;
};

export function OrderReceipt({
  order,
  store,
  heading,
  introduction,
  instructions,
  refresh,
  downloadDocument,
  children,
}: {
  order: Order;
  store: Store;
  heading: ReactNode;
  introduction: ReactNode;
  instructions?: string;
  refresh: OrderRefreshState;
  downloadDocument: (document: OrderDocument) => Promise<void>;
  children?: ReactNode;
}) {
  const { t } = useTranslation(["orders", "documents", "common", "cart"]);
  const money = (value: number) =>
    formatCurrency(value, store.culture, order.currency);
  const status = knownOrderStatus(order.status);
  const expectedDocument = expectedDocumentKind(order);

  const preparingDocument = expectedDocument !== null && !order.documents.some((document) => document.kind === expectedDocument);

  return (
    <section className="order-receipt" aria-labelledby="order-heading">
      <header className="order-receipt-header">
        <div>
          <p className="customer-eyebrow">{t("orders:receipt")}</p>
          <h1 id="order-heading">{heading}</h1>
          <div className="order-introduction">{introduction}</div>
        </div>
        <Link className="customer-shopping-link" to="/">{t("cart:continueShopping")}</Link>
      </header>
      <nav className="customer-sections" aria-label={t("orders:navigation")}>
        <a href="#order-status-heading">{t("orders:statusHeading")}</a>
        <a href="#order-items-heading">{t("orders:itemsHeading")}</a>
        <a href="#order-details-heading">{t("orders:detailsHeading")}</a>
        <a href="#order-documents-heading">{t("orders:documentsHeading")}</a>
        {children && <a href="#returns-heading">{t("orders:returnTitle")}</a>}
      </nav>
      <div className="order-receipt-layout">
        <section className="order-panel order-status-panel" aria-labelledby="order-status-heading">
          <div className="order-panel-heading">
            <h2 id="order-status-heading" tabIndex={-1}>{t("orders:statusHeading")}</h2>
            <OrderStatusBadge value={order.status} />
          </div>
          <div className="order-status-copy" aria-live="polite">
            <p>{t(statusNoticeKeys[status])}</p>
          </div>
          {status === "AwaitingPayment" && instructions && (
            <div className="order-instructions">
              <strong>{t("orders:paymentInstructions")}</strong>
              <p>{instructions}</p>
            </div>
          )}
          {order.shipment && <Shipment order={order} />}
          {refresh.refreshing && <p className="hint order-refreshing" role="status">{t("orders:refreshing")}</p>}
          {refresh.error !== null && <RequestError error={refresh.error} operation="read" onRetry={refresh.onRefresh} />}
          {refresh.waiting && refresh.timedOut && <p className="hint">{t("orders:pollTimedOut")}</p>}
          {refresh.waiting && !refresh.timedOut && !refresh.refreshing && refresh.error === null && (
            <p className="hint order-polling">{t("orders:checkingUpdates")}</p>
          )}
          <button type="button" className="link-button order-refresh" disabled={refresh.refreshing} onClick={refresh.onRefresh}>
            {t("orders:refresh")}
          </button>
        </section>

        <section className="order-panel order-items" aria-labelledby="order-items-heading">
          <h2 id="order-items-heading" tabIndex={-1}>{t("orders:itemsHeading")}</h2>
          <ul className="order-item-list" aria-label={t("orders:itemsHeading")}>
            {order.lines.map((line, index) => (
              <li key={line.productName + ":" + index}>
                <span className="order-line-copy">
                  <strong lang={store.culture}>{line.productName}</strong>
                  <span className="hint">{t("orders:lineCalculation", { quantity: line.quantity, price: money(line.unitPrice) })}</span>
                </span>
                <strong>{money(line.lineTotal)}</strong>
              </li>
            ))}
          </ul>
          <dl className="order-totals">
            <div><dt>{t("orders:items")}</dt><dd>{money(order.itemsTotal)}</dd></div>
            <div><dt lang={store.culture}>{order.shippingMethod}</dt><dd>{money(order.shippingPrice)}</dd></div>
            <div className="order-grand-total"><dt>{t("orders:total")}</dt><dd>{money(order.grandTotal)}</dd></div>
          </dl>
          {order.discount && (
            <p className="order-discount-note">
              {t(order.discount.amount > 0 ? "orders:discountApplied" : "orders:discountRecorded", {
                name: order.discount.name, code: order.discount.code, amount: money(order.discount.amount),
              })}
            </p>
          )}
        </section>

        <section className="order-panel order-details" aria-labelledby="order-details-heading">
          <h2 id="order-details-heading" tabIndex={-1}>{t("orders:detailsHeading")}</h2>
          <dl>
            <div><dt>{t("orders:placedLabel")}</dt><dd><time dateTime={order.placedAt}>{formatDateTime(order.placedAt, store.culture)}</time></dd></div>
            <div><dt>{t("orders:emailLabel")}</dt><dd><bdi>{order.email}</bdi></dd></div>
            <div><dt>{t("orders:paymentLabel")}</dt><dd lang={store.culture}>{order.paymentMethod}</dd></div>
            <div><dt>{t("orders:shippingLabel")}</dt><dd lang={store.culture}>{order.shippingMethod}</dd></div>
            {order.pickupPoint && <div><dt>{t("orders:pickupLabel")}</dt><dd lang={store.culture}>{order.pickupPoint}</dd></div>}
            <div><dt>{t("orders:vatLabel")}</dt><dd>{money(order.vatTotal)}</dd></div>
          </dl>
        </section>

        <section className="order-panel order-documents" aria-labelledby="order-documents-heading">
          <h2 id="order-documents-heading" tabIndex={-1}>{t("orders:documentsHeading")}</h2>
          {order.documents.length > 0 && (
            <ul>
              {order.documents.map((document) => {
                const kind = knownDocumentKind(document.kind);
                return (
                  <li key={document.number}>
                    <div className="order-document-meta">
                      <strong>{t(documentKeys[kind])} <span className="order-document-format">PDF</span></strong>
                      <bdi className="order-document-number">{document.number}</bdi>
                      <time className="hint" dateTime={document.issuedAt}>{formatDateTime(document.issuedAt, store.culture)}</time>
                    </div>
                    <DownloadButton download={() => downloadDocument(document)}>
                      {t("orders:downloadDocument", { kind: t(documentKeys[kind]), number: document.number })}
                    </DownloadButton>
                  </li>
                );
              })}
            </ul>
          )}
          {preparingDocument ? (
            <p className="hint order-document-waiting" role="status">
              {t(expectedDocument === "Invoice" ? "orders:invoicePreparing" : "orders:creditNotePreparing")}
            </p>
          ) : order.documents.length === 0 && (
            <p className="hint">{t(status === "AwaitingPayment" ? "orders:documentsAfterPayment" : "orders:noDocuments")}</p>
          )}
        </section>
      </div>
      {children}
    </section>
  );
}

function Shipment({ order }: { order: Order }) {
  const { t } = useTranslation("orders");
  const shipment = order.shipment!;
  const trackingUrl = safeTrackingUrl(shipment.trackingUrl);

  return (
    <div className="order-shipment">
      <strong>{t("shipmentHeading")}</strong>
      <p>
        {trackingUrl ? (
          <>
            {t("shipment", {
              carrier: shipment.carrier,
              trackingNumber: shipment.trackingNumber,
            })}{" "}
            <a href={trackingUrl} target="_blank" rel="noreferrer">
              {t("trackNewTab")}
            </a>
          </>
        ) : (
          t("shipment", {
            carrier: shipment.carrier,
            trackingNumber: shipment.trackingNumber,
          })
        )}
      </p>
    </div>
  );
}
