import { useTranslation } from "react-i18next";
import type { AdminOrderDetail, OrderReturn } from "../api";
import { currencyFormatter, formatDateTime } from "../utils/format";
import { ReturnLines } from "./ReturnLines";
import { refundStatuses, returnStatuses } from "../utils/returnQuery";

export function ReturnStatuses({ status, refund }: { status: string; refund: string | null }) {
  const { t } = useTranslation(["returnReview", "returns", "orderDetails"]);
  const handling = returnStatuses.find((value) => value === status) ?? "unknown";
  const money = refundStatuses.find((value) => value === refund) ?? "unknown";
  return <dl className="order-facts return-status-facts">
    <div><dt>{t("handlingStatus")}</dt><dd><span className="badge">{t(`returns:statuses.${handling}`)}{handling === "unknown" && ` (${status})`}</span></dd></div>
    <div><dt>{t("refundStatus")}</dt><dd><span className="badge">{refund === null ? t("orderDetails:notRecorded") : t(`orderDetails:refundStatus.${money}`)}{refund !== null && money === "unknown" && ` (${refund})`}</span></dd></div>
  </dl>;
}

export function ReturnReviewFacts({ record, order }: { record: OrderReturn; order: AdminOrderDetail }) {
  const { t, i18n } = useTranslation(["returnReview", "returns", "orderDetails", "orders"]);
  const facts = record;
  const handling = returnStatuses.find((value) => value === facts.status) ?? "unknown";
  const money = facts.refund === null ? "none" : refundStatuses.find((value) => value === facts.refund) ?? "unknown";
  return <>
    <section className="order-panel" aria-labelledby="return-state-title">
      <h2 id="return-state-title">{t("stateTitle")}</h2><ReturnStatuses status={facts.status} refund={facts.refund} />
      <p>{t(`handlingHint.${handling}`)}</p><p className="hint">{t(`moneyHint.${money}`)}</p>
      <dl className="order-facts">
        <div><dt>{t("orderDetails:creditedAmount")}</dt><dd>{currencyFormatter(order.currency, i18n.resolvedLanguage).format(facts.refundedAmount)}</dd></div>
        <div><dt>{t("orders:customer")}</dt><dd>{order.email}</dd></div>
        <div><dt>{t("returns:requested")}</dt><dd><time dateTime={record.requestedAt}>{formatDateTime(record.requestedAt, i18n.resolvedLanguage)}</time></dd></div>
        <div><dt>{t("decidedAt")}</dt><dd>{record.decidedAt === null ? t("orderDetails:notRecorded") : <time dateTime={record.decidedAt}>{formatDateTime(record.decidedAt, i18n.resolvedLanguage)}</time>}</dd></div>
        <div><dt>{t("receivedAt")}</dt><dd>{record.receivedAt === null ? t("orderDetails:notRecorded") : <time dateTime={record.receivedAt}>{formatDateTime(record.receivedAt, i18n.resolvedLanguage)}</time>}</dd></div>
        {(record.refund === "Refunded" || record.refund === "ClosedByHand") && <div><dt>{t(record.refund === "ClosedByHand" ? "closedAt" : "refundSettledAt")}</dt><dd>{record.refundSettledAt === null ? t("orderDetails:notRecorded") : <time dateTime={record.refundSettledAt}>{formatDateTime(record.refundSettledAt, i18n.resolvedLanguage)}</time>}</dd></div>}
      </dl>
      {facts.refundProblem !== null && <p className="return-record-text">{t("orderDetails:refundProblem")}: {facts.refundProblem}</p>}
      {record.refundNote !== null && <p className="return-record-text">{t("refundNote")}: {record.refundNote}</p>}
    </section>
    <section className="order-panel" aria-labelledby="return-items-title">
      <h2 id="return-items-title">{t("itemsTitle")}</h2><p className="hint">{t("itemIdentityHint")}</p>
      <ReturnLines lines={record.lines} />
      <h3>{t("reasonTitle")}</h3><p className="return-record-text">{record.reason ?? t("noReason")}</p>
    </section>
  </>;
}
