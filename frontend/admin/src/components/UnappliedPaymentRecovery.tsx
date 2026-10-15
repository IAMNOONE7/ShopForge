import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api } from "../api";
import { HttpError } from "../api/http";
import { useRequest } from "../useRequest";
import { canChangePayment, type UnappliedAttempt } from "../utils/unappliedAttempt";
import { UnappliedPaymentConfirmation } from "./UnappliedPaymentConfirmation";
import { UnappliedPaymentFacts } from "./UnappliedPaymentFacts";
import { Button } from "./ui/Button";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

export function UnappliedPaymentRecovery({ storeId, attempt, onRetry, onFinish }: {
  storeId: string; attempt: UnappliedAttempt; onRetry: (attempt: UnappliedAttempt) => Promise<boolean>; onFinish: () => void;
}) {
  const { t } = useTranslation(["unapplied", "orders"]);
  const [confirming, setConfirming] = useState(false);
  const [state, reload] = useRequest(`unapplied-recovery:${storeId}:${attempt.key}`, async (signal) => {
    const order = await api.order(storeId, attempt.orderNumber, signal);
    const payment = order.unappliedPayments.find((item) => item.id === attempt.id);
    if (!payment) throw new HttpError("invalid-response", 200);
    return payment;
  });
  const ready = state.status === "ready" ? state : null;
  const fresh = ready !== null && !ready.refreshing && ready.refreshError === null;
  return <section className="order-panel unapplied-recovery" aria-labelledby="unapplied-recovery-title">
    <h2 id="unapplied-recovery-title" tabIndex={-1}>{t("recoveryTitle")}</h2><p>{t("recoveryHint")}</p>
    <Link to={`/stores/${encodeURIComponent(storeId)}/orders/${encodeURIComponent(attempt.orderNumber)}#order-unapplied-${encodeURIComponent(attempt.id)}`}>{t("openOrder", { number: attempt.orderNumber })}</Link>
    {state.status === "loading" && <LoadingState label={t("recoveryLoading")} />}
    {(state.status === "error" || state.status === "not-found") && <RequestError error={state.error} operation="read" onRetry={reload} />}
    {ready && <>
      {ready.refreshError !== null && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
      <UnappliedPaymentFacts payment={ready.data} />
      {fresh && canChangePayment(ready.data, attempt.action) ? <>
        <p>{t("sameKeyHint")}</p>
        {!confirming ? <Button type="button" variant="secondary" onClick={() => setConfirming(true)}>{t("reviewRecovery")}</Button>
          : <UnappliedPaymentConfirmation payment={ready.data} action={attempt.action} disabled={!fresh} pending={false} savedNote={attempt.note ?? undefined}
              onConfirm={() => onRetry(attempt)} onClose={() => { setConfirming(false); document.getElementById("unapplied-recovery-title")?.focus(); }} />}
      </> : <>
        {fresh && <p>{t("recoveryNoResend")}</p>}
        <Button type="button" variant="secondary" disabled={!fresh} onClick={onFinish}>{t("finishReview")}</Button>
      </>}
      <Button type="button" variant="secondary" disabled={ready.refreshing} onClick={() => { setConfirming(false); reload(); }}>{t("orders:refresh")}</Button>
    </>}
  </section>;
}
