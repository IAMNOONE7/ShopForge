import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { AdminOrderReturn } from "../api";
import { currencyFormatter } from "../utils/format";
import type { RefundOutcome as Outcome } from "../utils/refundWorkflow";
import { refundStatuses, returnReviewPath } from "../utils/returnQuery";
import { InlineMessage } from "./ui/InlineMessage";

export function RefundRecords({ storeId, number, currency, records }: { storeId: string; number: string; currency: string; records: AdminOrderReturn[] }) {
  const { t, i18n } = useTranslation(["refunds", "orderDetails", "returnReview"]);
  return <ul className="refund-outcomes">{records.map((record) => {
    const state = record.refund === null ? "none" : refundStatuses.find((value) => value === record.refund) ?? "unknown";
    return <li key={record.id}><Link to={returnReviewPath(storeId, record.id, number)}>{record.number}</Link>
      <p>{t("orderDetails:creditedAmount")}: {currencyFormatter(currency, i18n.resolvedLanguage).format(record.refundedAmount)}</p>
      <p>{t("returnReview:refundStatus")}: {state === "none" ? t("orderDetails:notRecorded") : t(`orderDetails:refundStatus.${state}`)}</p>
      <p>{t(`returnReview:moneyHint.${state}`)}</p>
      {record.refundProblem !== null && <p className="return-record-text">{t("orderDetails:refundProblem")}: {record.refundProblem}</p>}
    </li>;
  })}</ul>;
}
export function RefundOutcome({ storeId, outcome }: { storeId: string; outcome: Outcome }) {
  const { t } = useTranslation("refunds");
  return <div className="refund-outcome"><InlineMessage>{t(`acknowledged.${outcome.attempt.action}`)}</InlineMessage>
    <RefundRecords storeId={storeId} number={outcome.attempt.orderNumber} currency={outcome.attempt.currency} records={outcome.records} />
  </div>;
}
