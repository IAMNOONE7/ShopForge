import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams, useSearchParams } from "react-router";
import { useSelectedStore } from "../adminContext";
import { api, type AdminOrderDetail, type AdminOrderReturn, type OrderReturn } from "../api";
import { HttpError, isHttpError } from "../api/http";
import type { ReturnDecision } from "../api/returns";
import { ReturnDecisionPanel } from "../components/ReturnDecisionPanel";
import { ReturnReviewFacts, ReturnStatuses } from "../components/ReturnReviewFacts";
import { RefundActions } from "../components/RefundActions";
import { RefundOutcome } from "../components/RefundOutcome";
import { RefundRecovery } from "../components/RefundRecovery";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { useRefundAction } from "../useRefundAction";
import { refundProblem } from "../utils/refundWorkflow";
import { formatDateTime, formatNumber } from "../utils/format";
import { canDecideReturn } from "../utils/returnDecision";
import { refundStatuses, returnLimit, returnParameters, returnQuery, returnReviewPath, returnStatuses, type ReturnQuery } from "../utils/returnQuery";

export function StoreReturnsPage() {
  const { store } = useSelectedStore();
  return <ReturnList key={store.id} storeId={store.id} />;
}
function ReturnList({ storeId }: { storeId: string }) {
  const { t, i18n } = useTranslation(["returnReview", "returns", "orders", "orderDetails"]);
  const [parameters, setParameters] = useSearchParams();
  const { status, refund, invalid } = returnQuery(parameters);
  const [state, reload] = useRequest(`returns:${storeId}:${status}:${refund}`, (signal) => api.returns(storeId, signal, { status, refund }));
  const ready = state.status === "ready" ? state : null;
  const summary = useRef<HTMLParagraphElement>(null); const filterFocus = useRef(false);
  useEffect(() => { if (state.status === "ready" && filterFocus.current) { filterFocus.current = false; summary.current?.focus(); } }, [state]);
  function filter(query: ReturnQuery) { filterFocus.current = true; setParameters(returnParameters(query)); }
  return <div className="orders-page returns-page">
    <div className="order-page-heading"><div><h1>{t("returns:title")}</h1><p className="hint">{t("intro")}</p></div>
      <Button type="button" variant="secondary" disabled={state.status === "loading" || ready?.refreshing} onClick={reload}>{t("orders:refresh")}</Button></div>
    <section className="order-list-controls" aria-label={t("filtersTitle")}>
      <div className="order-sort"><label htmlFor="return-status">{t("handlingStatus")}</label><select id="return-status" value={status} onChange={(event) => filter({ status: event.target.value as ReturnQuery["status"], refund })}>
        <option value="">{t("allHandling")}</option>{returnStatuses.map((value) => <option key={value} value={value}>{t(`returns:statuses.${value}`)}</option>)}
      </select></div>
      <div className="order-sort"><label htmlFor="return-refund">{t("refundStatus")}</label><select id="return-refund" value={refund} onChange={(event) => filter({ status, refund: event.target.value as ReturnQuery["refund"] })}>
        <option value="">{t("allRefunds")}</option>{refundStatuses.map((value) => <option key={value} value={value}>{t(`orderDetails:refundStatus.${value}`)}</option>)}
      </select></div>
      {(status || refund) && <Link to={`/stores/${encodeURIComponent(storeId)}/returns`}>{t("clearFilters")}</Link>}
    </section>
    {invalid && <InlineMessage>{t("invalidFilters")} <Link to={`/stores/${encodeURIComponent(storeId)}/returns`}>{t("clearFilters")}</Link></InlineMessage>}
    <p className="hint">{t("collectionLimit")}</p>
    {state.status === "loading" && <LoadingState label={t("loadingList")} lines={4} />}
    {(state.status === "error" || state.status === "not-found") && <RequestError error={state.error} operation="read" onRetry={reload} />}
    {ready && <>
      <p ref={summary} tabIndex={-1} role="status">{t("shown", { number: formatNumber(ready.data.length, i18n.resolvedLanguage) })}</p>
      {ready.data.length === returnLimit && <InlineMessage>{t("atLimit")}</InlineMessage>}
      {ready.refreshing && <p role="status">{t("orders:refreshing")}</p>}
      {ready.refreshError !== null && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
      {(ready.refreshing || ready.refreshError !== null) && <p className="hint">{t("orders:staleList")}</p>}
      {ready.data.length === 0 ? <EmptyState headingLevel={2} title={t(status || refund ? "emptyFilteredTitle" : "emptyTitle")}><p>{t(status || refund ? "emptyFilteredHint" : "emptyHint")}</p></EmptyState>
        : <ul className="return-list" aria-label={t("returns:title")} aria-busy={ready.refreshing}>
          {ready.data.map((record) => <li className="order-panel" key={record.id}>
            <div className="order-record-heading"><h2><Link to={returnReviewPath(storeId, record.id, record.orderNumber, { status, refund })}>{record.number}</Link></h2>
              <Link to={`/stores/${encodeURIComponent(storeId)}/orders/${encodeURIComponent(record.orderNumber)}#order-return-${encodeURIComponent(record.id)}`}>{t("orderLink", { number: record.orderNumber })}</Link></div>
            <ReturnStatuses status={record.status} refund={record.refund} />
            <dl className="order-facts"><div><dt>{t("returns:requested")}</dt><dd><time dateTime={record.requestedAt}>{formatDateTime(record.requestedAt, i18n.resolvedLanguage)}</time></dd></div>
              <div><dt>{t("itemsTitle")}</dt><dd>{t("lineCount", { number: formatNumber(record.lines.length, i18n.resolvedLanguage) })}</dd></div></dl>
            {record.refundProblem !== null && <p className="return-record-text">{t("orderDetails:refundProblem")}: {record.refundProblem}</p>}
          </li>)}
        </ul>}
    </>}
  </div>;
}

type ReturnReview = { record: OrderReturn | null; order: AdminOrderDetail | null; summary: AdminOrderReturn | null };
export function StoreReturnPage() {
  const { store } = useSelectedStore(); const { user } = useSession(); const { returnId = "" } = useParams(); const [parameters] = useSearchParams();
  const orderHint = parameters.get("order") ?? "";
  return <ReturnWorkspace key={`${user.id}:${store.id}:${returnId}:${orderHint}`} storeId={store.id} userId={user.id} id={returnId} orderHint={orderHint} canManage={canManageStore(user.role)} />;
}
function ReturnWorkspace({ storeId, userId, id, orderHint, canManage }: { storeId: string; userId: string; id: string; orderHint: string; canManage: boolean }) {
  const { t } = useTranslation(["returnReview", "returns", "orders", "refunds"]);
  const [parameters] = useSearchParams(); const query = returnParameters(returnQuery(parameters));
  const [state, reload] = useRequest(`return:${storeId}:${id}:${orderHint}`, async (signal): Promise<ReturnReview> => {
    const records = await api.returns(storeId, signal);
    const record = records.find((item) => item.id === id) ?? null;
    const number = record?.orderNumber ?? orderHint;
    if (!number) return { record, order: null, summary: null };
    const order = await api.order(storeId, number, signal);
    const summary = order.returns.find((item) => item.id === id) ?? null;
    if (order.number !== number || (record && (!summary || summary.number !== record.number))) throw new HttpError("invalid-response", 200);
    return { record, order, summary };
  });
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const [stale, setStale] = useState<ReturnReview | null>(null);
  const [success, setSuccess] = useState<ReturnDecision | null>(null);
  const [decisionRevision, setDecisionRevision] = useState(0);
  const mounted = useRef(false); const result = useRef<HTMLDivElement>(null);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const ready = state.status === "ready" ? state : null;
  const record = ready?.data.record;
  const money = useRefundAction({ storeId, userId, allowed: canManage, run, pending, onChanged: () => { setStale(ready?.data ?? null); reload(); } });
  useEffect(() => { if (error !== null || success !== null || money.outcome !== null) result.current?.focus(); }, [error, success, money.outcome]);
  const readStale = !ready || ready.refreshing || ready.refreshError !== null || ready.data === stale;
  const disabled = pending || readStale || money.blocked || money.attempt !== null;
  const problem = refundProblem(error);
  const noteError = isHttpError(error) && error.status === 400 && Boolean(error.problem?.errors?.note);
  async function decide(decision: ReturnDecision) {
    if (disabled || !canManage || !ready || !record || !canDecideReturn(record, decision)) return;
    setSuccess(null);
    money.clearOutcome();
    await run(async () => {
      try {
        const changed = await api.decideReturn(storeId, id, decision);
        if (changed.number !== record.number || changed.orderNumber !== record.orderNumber) throw new HttpError("invalid-response", 200);
        if (mounted.current) { setSuccess(decision); setDecisionRevision((version) => version + 1); }
      } finally {
        if (mounted.current) { setStale(ready.data); reload(); }
      }
    });
  }
  return <div className="orders-page returns-page">
    <Link className="physical-back-link" to={`/stores/${encodeURIComponent(storeId)}/returns${query.size ? `?${query}` : ""}`}>{t("backToReturns")}</Link>
    <div className="order-page-heading"><h1>{record?.number ?? ready?.data.summary?.number ?? t("detailTitle")}</h1>
      <Button type="button" variant="secondary" disabled={pending || state.status === "loading" || ready?.refreshing} onClick={reload}>{t("orders:refresh")}</Button></div>
    {state.status === "loading" && <LoadingState label={t("loadingDetail")} lines={4} />}
    {(state.status === "error" || state.status === "not-found") && <RequestError error={state.error} operation="read" onRetry={reload} />}
    <div ref={result} tabIndex={-1} className="order-action-result">
      {success && <InlineMessage tone="success">{t(`success.${success}`)}</InlineMessage>}
      {money.outcome && <RefundOutcome storeId={storeId} outcome={money.outcome} />}
      {money.blocked && <InlineMessage tone="error">{t("refunds:storageBlocked")}</InlineMessage>}
      {error !== null && <>{isHttpError(error) && error.status === 409 && error.problem?.title === "This return has already been dealt with" && <p>{t("conflict")}</p>}
        {problem && <p>{t(`refunds:problems.${problem}`)}</p>}
        <RequestError error={error} operation="write" fieldErrors={noteError ? [{ field: "refund-note", message: t("refunds:noteInvalid") }] : undefined} /><p className="hint">{t("writeFailure")}</p></>}
      {pending && <p role="status">{t("orders:saving")}</p>}
    </div>
    {canManage && money.attempt && !pending && !money.blocked && <RefundRecovery key={money.attempt.key} storeId={storeId} attempt={money.attempt} onExecute={money.execute} onFinish={money.finishReview} />}
    {ready && <>
      {ready.refreshing && <p role="status">{t("orders:refreshingDetail")}</p>}
      {ready.refreshError !== null && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
      {readStale && !pending && <p role="status" className="hint">{t("staleDetail")}</p>}
      {ready.data.order && <Link to={`/stores/${encodeURIComponent(storeId)}/orders/${encodeURIComponent(ready.data.order.number)}${ready.data.summary ? `#order-return-${encodeURIComponent(id)}` : ""}`}>{t("orderLink", { number: ready.data.order.number })}</Link>}
      {!record && <EmptyState headingLevel={2} title={t("outsideTitle")}><p>{t(ready.data.summary ? "summaryOnly" : "outsideHint")}</p></EmptyState>}
      {ready.data.summary && ready.data.order && <ReturnReviewFacts record={record ?? null} summary={ready.data.summary} order={ready.data.order} />}
      {record && canManage && <ReturnDecisionPanel key={`${id}:${decisionRevision}`} record={record} snapshot={ready.data} disabled={disabled} pending={pending} onDecide={decide} />}
      {record && ready.data.order && canManage && (!money.attempt || pending) && <RefundActions key={`money:${money.revision}`} target="return" order={ready.data.order} record={record} snapshot={ready.data}
        disabled={disabled} pending={pending} noteError={noteError} onExecute={async (attempt) => { setSuccess(null); await money.execute(attempt); }} />}
      {!canManage && <p className="hint">{t("readOnly")}</p>}
    </>}
  </div>;
}
