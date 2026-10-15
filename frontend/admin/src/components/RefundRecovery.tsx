import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api } from "../api";
import { HttpError } from "../api/http";
import { useRequest } from "../useRequest";
import { canRecoverRefund, type RefundAttempt } from "../utils/refundWorkflow";
import { RefundConfirmation } from "./RefundConfirmation";
import { RefundRecords } from "./RefundOutcome";
import { Button } from "./ui/Button";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

export function RefundRecovery({ storeId, attempt, onExecute, onFinish }: { storeId: string; attempt: RefundAttempt; onExecute: (attempt: RefundAttempt) => Promise<void>; onFinish: () => void }) {
  const { t } = useTranslation(["refunds", "orders", "returnReview"]);
  const [confirming, setConfirming] = useState(false);
  const [state, reload] = useRequest(`refund-recovery:${storeId}:${attempt.key}`, async (signal) => {
    const order = await api.order(storeId, attempt.orderNumber, signal);
    if (order.number !== attempt.orderNumber || order.currency !== attempt.currency) throw new HttpError("invalid-response", 200);
    const record = attempt.returnId === null ? null : (await api.returns(storeId, signal)).find((item) => item.id === attempt.returnId) ?? null;
    const summary = attempt.returnId === null ? null : order.returns.find((item) => item.id === attempt.returnId) ?? null;
    if (attempt.returnId !== null && (!summary || summary.number !== attempt.returnNumber || (record && (record.orderNumber !== order.number || record.number !== summary.number)))) throw new HttpError("invalid-response", 200);
    const records = summary ? [summary] : order.returns.filter((item) => !attempt.previousReturnIds.includes(item.id));
    return { order, record, records };
  });
  const ready = state.status === "ready" ? state : null;
  const fresh = ready !== null && !ready.refreshing && ready.refreshError === null;
  const replay = ready && canRecoverRefund(attempt, ready.data.order, ready.data.record);
  return <section className="order-panel refund-recovery" aria-labelledby="refund-recovery-title">
    <h2 id="refund-recovery-title" tabIndex={-1}>{t("recoveryTitle")}</h2><p>{t("recoveryHint")}</p>
    <Link to={`/stores/${encodeURIComponent(storeId)}/orders/${encodeURIComponent(attempt.orderNumber)}`}>{t("returnReview:orderLink", { number: attempt.orderNumber })}</Link>
    {state.status === "loading" && <LoadingState label={t("recoveryLoading")} />}
    {(state.status === "error" || state.status === "not-found") && <RequestError error={state.error} operation="read" onRetry={reload} />}
    {ready && <>
      {ready.refreshError !== null && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
      <RefundRecords storeId={storeId} number={ready.data.order.number} currency={ready.data.order.currency} records={ready.data.records} />
      {fresh && replay ? <><p>{t("sameKey")}</p>
        {confirming ? <RefundConfirmation action={attempt.action} order={ready.data.order} record={ready.data.record} disabled={!fresh} pending={false} savedNote={attempt.note ?? undefined}
          onConfirm={() => onExecute(attempt)} onClose={() => { setConfirming(false); document.getElementById("refund-recovery-title")?.focus(); }} />
          : <Button type="button" variant="secondary" onClick={() => setConfirming(true)}>{t("reviewSame")}</Button>}
      </> : <><p>{t("noReplay")}</p><Button type="button" variant="secondary" disabled={!fresh} onClick={onFinish}>{t("finishReview")}</Button></>}
      <Button type="button" variant="secondary" disabled={ready.refreshing} onClick={() => { setConfirming(false); reload(); }}>{t("orders:refresh")}</Button>
    </>}
  </section>;
}
