import { useTranslation } from "react-i18next";
import { knownRefundState, recordedDate } from "../../refundFacts";
import { formatDateTime } from "../../utils/format";

export function RefundStatus({ state, settledAt, culture }: { state: string | null; settledAt: string | null; culture: string }) {
  const { t } = useTranslation("orders");
  const known = knownRefundState(state);
  const date = known === "Settled" ? recordedDate(settledAt) : null;
  return <div className="refund-progress">
    <p className="refund-state" data-state={known}>{t(`refundState.${known}`)}</p>
    {known === "Settled" ? <p className="hint refund-settled-at">
      {date ? <>{t("refundSettledAt")} <time dateTime={date}>{formatDateTime(date, culture)}</time></> : t("refundTimeNotRecorded")}
    </p> : <p className="hint">{t(known === "unknown" ? "returnCreditHint" : `refundExplanation.${known}`)}</p>}
  </div>;
}
