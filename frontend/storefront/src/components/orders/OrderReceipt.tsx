import type { ReactNode } from "react";
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
  const { t } = useTranslation(["orders", "documents", "common"]);
  const money = (value: number) =>
    formatCurrency(value, store.culture, order.currency);
  const status = knownOrderStatus(order.status);
  const expectedDocument = expectedDocumentKind(order);

  return (
    <section className="order-receipt" aria-labelledby="order-heading">
      <header className="order-receipt-header">
        <h1 id="order-heading">{heading}</h1>
        <div className="order-introduction">{introduction}</div>
      </header>

      <div className="order-receipt-layout">
        <aside className="order-side">
          <section
            className="order-panel order-status-panel"
            aria-labelledby="order-status-heading"
          >
            <div className="order-panel-heading">
              <h2 id="order-status-heading">{t("orders:statusHeading")}</h2>
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
            {refresh.refreshing && (
              <p className="hint order-refreshing" role="status">
                {t("orders:refreshing")}
              </p>
            )}
            {refresh.error !== null && (
              <RequestError
                error={refresh.error}
                operation="read"
                onRetry={refresh.onRefresh}
              />
            )}
            {refresh.waiting && refresh.timedOut && (
              <p className="hint">{t("orders:pollTimedOut")}</p>
            )}
            <button
              type="button"
              className="link-button order-refresh"
              disabled={refresh.refreshing}
              onClick={refresh.onRefresh}
            >
              {t("orders:refresh")}
            </button>
          </section>

          <section
            className="order-panel order-documents"
            aria-labelledby="order-documents-heading"
          >
            <h2 id="order-documents-heading">{t("orders:documentsHeading")}</h2>
            {order.documents.length > 0 ? (
              <ul>
                {order.documents.map((document) => {
                  const kind = knownDocumentKind(document.kind);
                  return (
                    <li key={document.number}>
                      <span>
                        <strong>{t(documentKeys[kind])}</strong>{" "}
                        <span className="order-document-number">
                          {document.number}
                        </span>
                        <span className="hint">
                          {" "}
                          ·{" "}
                          <time dateTime={document.issuedAt}>
                            {formatDateTime(document.issuedAt, store.culture)}
                          </time>
                        </span>
                      </span>
                      <DownloadButton
                        download={() => downloadDocument(document)}
                      >
                        {t("orders:downloadDocument", {
                          kind: t(documentKeys[kind]),
                          number: document.number,
                        })}
                      </DownloadButton>
                    </li>
                  );
                })}
              </ul>
            ) : (
              <p className="hint">
                {expectedDocument
                  ? t(
                      expectedDocument === "Invoice"
                        ? "orders:invoicePreparing"
                        : "orders:creditNotePreparing",
                    )
                  : status === "AwaitingPayment"
                    ? t("orders:documentsAfterPayment")
                    : t("orders:noDocuments")}
              </p>
            )}
          </section>
        </aside>

        <div className="order-main">
          <section
            className="order-panel order-items"
            aria-labelledby="order-items-heading"
          >
            <h2 id="order-items-heading">{t("orders:itemsHeading")}</h2>
            <ul className="order-item-list">
              {order.lines.map((line, index) => (
                <li key={line.productName + ":" + index}>
                  <span className="order-line-copy">
                    <strong lang={store.culture}>{line.productName}</strong>
                    <span className="hint">
                      {t("orders:lineCalculation", {
                        quantity: line.quantity,
                        price: money(line.unitPrice),
                      })}
                    </span>
                  </span>
                  <strong>{money(line.lineTotal)}</strong>
                </li>
              ))}
            </ul>
            <dl className="order-totals">
              <div>
                <dt>{t("orders:items")}</dt>
                <dd>{money(order.itemsTotal)}</dd>
              </div>
              <div>
                <dt>{order.shippingMethod}</dt>
                <dd>{money(order.shippingPrice)}</dd>
              </div>
              <div className="order-grand-total">
                <dt>{t("orders:total")}</dt>
                <dd>{money(order.grandTotal)}</dd>
              </div>
            </dl>
            {order.discount && (
              <p className="order-discount-note">
                {t("orders:discountApplied", {
                  name: order.discount.name,
                  code: order.discount.code,
                  amount: money(order.discount.amount),
                })}
              </p>
            )}
          </section>

          <section
            className="order-panel order-details"
            aria-labelledby="order-details-heading"
          >
            <h2 id="order-details-heading">{t("orders:detailsHeading")}</h2>
            <dl>
              <div>
                <dt>{t("orders:placedLabel")}</dt>
                <dd>
                  <time dateTime={order.placedAt}>
                    {formatDateTime(order.placedAt, store.culture)}
                  </time>
                </dd>
              </div>
              <div>
                <dt>{t("orders:emailLabel")}</dt>
                <dd>{order.email}</dd>
              </div>
              <div>
                <dt>{t("orders:paymentLabel")}</dt>
                <dd>{order.paymentMethod}</dd>
              </div>
              <div>
                <dt>{t("orders:shippingLabel")}</dt>
                <dd>{order.shippingMethod}</dd>
              </div>
              {order.pickupPoint && (
                <div>
                  <dt>{t("orders:pickupLabel")}</dt>
                  <dd>{order.pickupPoint}</dd>
                </div>
              )}
              <div>
                <dt>{t("orders:vatLabel")}</dt>
                <dd>{money(order.vatTotal)}</dd>
              </div>
            </dl>
          </section>
        </div>
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
