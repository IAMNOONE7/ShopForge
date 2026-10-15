import { useTranslation } from "react-i18next";
import type { AdminUnappliedPayment } from "../api";
import { currencyFormatter, formatDateTime } from "../utils/format";
import { unappliedStatuses } from "../utils/unappliedQuery";

export function UnappliedPaymentFacts({ payment }: { payment: AdminUnappliedPayment }) {
  const { t, i18n } = useTranslation(["orderDetails", "unapplied"]);
  const status = unappliedStatuses.find((value) => value === payment.status) ?? "unknown";
  const reason = (["OrderCancelled", "OrderAlreadyPaid"] as const).find((value) => value === payment.reason) ?? "unknown";
  return <>
    <span className="badge">{t(`unappliedStatus.${status}`)}{status === "unknown" && ` (${payment.status})`}</span>
    <dl className="order-facts">
      <div><dt>{t("amount")}</dt><dd>{currencyFormatter(payment.currency, i18n.resolvedLanguage).format(payment.amount)}</dd></div>
      <div><dt>{t("unapplied:provider")}</dt><dd>{payment.provider}</dd></div>
      <div><dt>{t("reference")}</dt><dd>{payment.reference || t("noReference")}</dd></div>
      <div><dt>{t("reason")}</dt><dd>{t(`unappliedReason.${reason}`)}{reason === "unknown" && ` (${payment.reason})`}</dd></div>
      <div><dt>{t("receivedAt")}</dt><dd><time dateTime={payment.receivedAt}>{formatDateTime(payment.receivedAt, i18n.resolvedLanguage)}</time></dd></div>
      <div><dt>{t("changedAt")}</dt><dd><time dateTime={payment.changedAt}>{formatDateTime(payment.changedAt, i18n.resolvedLanguage)}</time></dd></div>
    </dl>
    {payment.status === "Refunding" && <p className="hint">{t("unappliedRefundingHint")}</p>}
    {payment.status === "ClosedByHand" && <p className="hint">{t("closedByHandHint")}</p>}
    {payment.note !== null && <p className="order-record-note">{t("note")}: {payment.note}</p>}
  </>;
}
