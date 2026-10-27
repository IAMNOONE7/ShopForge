import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";
import { api, type AdminOrderDetail } from "../api";
import { HttpError, isHttpError } from "../api/http";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { useRefundAction } from "../useRefundAction";
import { refundProblem } from "../utils/refundWorkflow";
import { formatDateTime, formatNumber } from "../utils/format";
import { OrderActions } from "./OrderActions";
import { RefundActions } from "./RefundActions";
import { RefundOutcome } from "./RefundOutcome";
import { RefundRecovery } from "./RefundRecovery";
import { OrderDelivery, OrderItems, OrderPaymentAttempts, OrderPaymentRecord, OrderRelatedRecords } from "./OrderDetailRead";
import { canChangeOrder, orderActionProblem, type OrderAction } from "./orderDetailFacts";
import { OrderStatus } from "./OrderStatus";
import { OrderParcels } from "./OrderParcels";
import { Button } from "./ui/Button";
import { EmptyState } from "./ui/EmptyState";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

type Props = { storeId: string; userId: string; number: string; canManage: boolean };
export function OrderDetailsSection(props: Props) { return <OrderWorkspace key={`${props.userId}:${props.storeId}:${props.number}`} {...props} />; }

function OrderWorkspace({ storeId, userId, number, canManage }: Props) {
  const { t, i18n } = useTranslation(["orderDetails", "orders", "refunds", "orderParcels"]);
  const { hash } = useLocation();
  const [detail, reload] = useRequest(`order:${storeId}:${number}`, async (signal) => {
    const order = await api.order(storeId, number, signal);
    if (order.number !== number) throw new HttpError("invalid-response", 200);
    return { order };
  });
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const [staleSnapshot, setStaleSnapshot] = useState<{ order: AdminOrderDetail } | null>(null);
  const [action, setAction] = useState<OrderAction>("payment");
  const [success, setSuccess] = useState<OrderAction | null>(null);
  const mounted = useRef(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const result = useRef<HTMLDivElement>(null);
  const focusedHash = useRef("");
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const ready = detail.status === "ready" ? detail : null;
  const order = ready?.data.order;
  const money = useRefundAction({ storeId, userId, allowed: canManage, run, pending, onChanged: () => { setStaleSnapshot(ready?.data ?? null); reload(); } });
  useEffect(() => { if (error !== null || success !== null || money.outcome !== null) result.current?.focus(); }, [error, success, money.outcome]);
  const disabled = pending || !ready || ready.refreshing || ready.refreshError !== null || ready.data === staleSnapshot || money.attempt !== null || money.blocked;
  const problem = orderActionProblem(error, action);
  const moneyProblem = refundProblem(error);
  const noteError = isHttpError(error) && error.status === 400 && Boolean(error.problem?.errors?.note);
  const trackingError = action === "shipment" && isHttpError(error) && error.status === 400 && Boolean(error.problem?.errors?.trackingNumber);
  useEffect(() => {
    if (hash === focusedHash.current) return;
    focusedHash.current = "";
    if (!order || !hash) return;
    let id: string;
    try { id = decodeURIComponent(hash.slice(1)); } catch { return; }
    if (!["order-payment-title", "order-payment-attempts", "order-unapplied", "order-returns", "order-parcels-title", "order-actions-title", "order-refund-actions", ...order.unappliedPayments.map((payment) => `order-unapplied-${payment.id}`), ...order.returns.map((returned) => `order-return-${returned.id}`)].includes(id)) return;
    const target = document.getElementById(id);
    if (target) focusedHash.current = hash;
    target?.focus({ preventScroll: true }); target?.scrollIntoView({ block: "start" });
  }, [hash, order]);

  async function change(kind: OrderAction, tracking: string) {
    if (disabled || !canManage || !ready || !order || !canChangeOrder(order, kind)) return;
    setAction(kind); setSuccess(null);
    money.clearOutcome();
    await run(async () => {
      try {
        if (kind === "payment") await api.confirmOrderPayment(storeId, number);
        else if (kind === "cancel") await api.cancelOrder(storeId, number);
        else await api.createShipment(storeId, number, tracking);
        if (mounted.current) setSuccess(kind);
      } finally {
        if (mounted.current) { setStaleSnapshot(ready.data); reload(); }
      }
    });
  }

  return <>
    <div className="order-page-heading"><h1 ref={heading} tabIndex={-1}>{t("orders:detailTitle", { number })}</h1>
      <Button type="button" variant="secondary" disabled={pending || detail.status === "loading" || ready?.refreshing} onClick={() => { setSuccess(null); reload(); }}>{t("orders:refresh")}</Button></div>
    {detail.status === "loading" && <LoadingState label={t("orders:loading")} />}
    {detail.status === "not-found" && <EmptyState headingLevel={2} title={t("orders:missingTitle")} action={<Button type="button" variant="secondary" onClick={reload}>{t("orders:refresh")}</Button>}><p>{t("orders:missingBody")}</p></EmptyState>}
    {detail.status === "error" && <RequestError error={detail.error} operation="read" onRetry={reload} />}
    <div ref={result} tabIndex={-1} className="order-action-result">
      {success && <InlineMessage tone="success">{t(`success.${success}`)}</InlineMessage>}
      {error !== null && <>
        {problem && <p className="hint">{t(problem)}</p>}
        <RequestError error={error} operation="write" fieldErrors={noteError ? [{ field: "refund-note", message: t("refunds:noteInvalid") }] : trackingError ? [{ field: "trackingNumber", message: t("trackingInvalid") }] : undefined} />
        <p className="hint">{t("writeFailureHint")}</p>
      </>}
      {moneyProblem && <p>{t(`refunds:problems.${moneyProblem}`)}</p>}
      {money.outcome && <RefundOutcome storeId={storeId} outcome={money.outcome} />}
      {money.blocked && <InlineMessage tone="error">{t("refunds:storageBlocked")}</InlineMessage>}
    </div>
    {canManage && money.attempt && !pending && !money.blocked && <RefundRecovery key={money.attempt.key} storeId={storeId} attempt={money.attempt} onExecute={money.execute} onFinish={money.finishReview} />}
    {ready && order && <div className="order-detail">
      {ready.refreshing && <p role="status">{t("orders:refreshingDetail")}</p>}
      {ready.refreshError !== null && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
      {(ready.refreshing || ready.refreshError !== null || ready.data === staleSnapshot) && <p className="hint" role="status">{t("orders:staleDetail")}</p>}
      <div className="order-detail-summary"><OrderStatus status={order.status} /><p>{order.email}</p><p><time dateTime={order.placedAt}>{formatDateTime(order.placedAt, i18n.resolvedLanguage)}</time></p></div>
      <nav className="order-detail-jumps" aria-label={t("sections")}>
        <a href="#order-payment-title">{t("paymentTitle")}</a>
        <a href="#order-payment-attempts">{t("attemptsLink", { total: formatNumber(order.paymentAttempts.length, i18n.resolvedLanguage) })}</a>
        <a href="#order-unapplied">{t("unappliedLink", { total: formatNumber(order.unappliedPayments.length, i18n.resolvedLanguage) })}</a>
        <a href="#order-returns">{t("returnsLink", { total: formatNumber(order.returns.length, i18n.resolvedLanguage) })}</a>
        <a href="#order-parcels-title">{t("orderParcels:title")}</a>
        {canManage && <a href="#order-actions-title">{t("orders:actions")}</a>}
        {canManage && <a href="#order-refund-actions">{t("refunds:orderTitle")}</a>}
      </nav>
      <div className="order-detail-columns">
        <OrderItems order={order} />
        <OrderPaymentRecord order={order} />
      </div>
      <OrderDelivery order={order} storeId={storeId} />
      <OrderParcels order={order} />
      <OrderPaymentAttempts order={order} />
      <OrderRelatedRecords order={order} storeId={storeId} />
      {canManage ? <OrderActions order={order} disabled={disabled} pending={pending} trackingError={trackingError} onChange={change} onFocusHeading={() => heading.current?.focus()} /> : <p className="hint">{t("readOnly")}</p>}
      {canManage && (!money.attempt || pending) && <RefundActions key={`money:${money.revision}`} target="order" order={order} snapshot={ready.data} disabled={disabled} pending={pending} noteError={noteError}
        onExecute={async (attempt) => { setSuccess(null); await money.execute(attempt); }} />}
    </div>}
  </>;
}
