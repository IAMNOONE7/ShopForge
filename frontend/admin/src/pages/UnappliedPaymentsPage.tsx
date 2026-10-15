import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useSearchParams } from "react-router";
import { useSelectedStore } from "../adminContext";
import { api, type AdminList, type AdminUnappliedPayment } from "../api";
import { isHttpError } from "../api/http";
import type { UnappliedAction } from "../api/unappliedPayments";
import { UnappliedPaymentConfirmation } from "../components/UnappliedPaymentConfirmation";
import { UnappliedPaymentFacts } from "../components/UnappliedPaymentFacts";
import { UnappliedPaymentRecovery } from "../components/UnappliedPaymentRecovery";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { formatNumber } from "../utils/format";
import { attemptStorageKey, canChangePayment, newUnappliedAttempt, paymentProblem, readUnappliedAttempt, uncertainPaymentWrite, type UnappliedAttempt } from "../utils/unappliedAttempt";
import { maxUnappliedPage, unappliedParameters, unappliedQuery, unappliedStatuses, type UnappliedQuery } from "../utils/unappliedQuery";

type Snapshot = { list: AdminList<AdminUnappliedPayment> };
type Selection = { payment: AdminUnappliedPayment; action: UnappliedAction; snapshot: Snapshot; query: string };
export function UnappliedPaymentsPage() {
  const { store } = useSelectedStore(); const { user } = useSession();
  return <PaymentsWorkspace key={`${user.id}:${store.id}`} storeId={store.id} userId={user.id} canManage={canManageStore(user.role)} />;
}
function PaymentsWorkspace({ storeId, userId, canManage }: { storeId: string; userId: string; canManage: boolean }) {
  const { t, i18n } = useTranslation(["unapplied", "orders", "orderDetails"]);
  const [parameters, setParameters] = useSearchParams();
  const { page, status, invalid } = unappliedQuery(parameters);
  const queryKey = `${page}:${status}`;
  const [state, reload] = useRequest(`unapplied:${storeId}:${queryKey}`, async (signal): Promise<Snapshot> => ({ list: await api.unappliedPayments(storeId, { page, status }, signal) }));
  const storageKey = attemptStorageKey(userId, storeId);
  const [saved] = useState(() => { try { return { attempt: canManage ? readUnappliedAttempt(storageKey) : null, blocked: false }; } catch { return { attempt: null, blocked: true }; } });
  const [attempt, setAttempt] = useState(saved.attempt);
  const [storageBlocked, setStorageBlocked] = useState(saved.blocked);
  const [selection, setSelection] = useState<Selection | null>(null);
  const [stale, setStale] = useState<Snapshot | null>(null);
  const [success, setSuccess] = useState<UnappliedAction | null>(null);
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const mounted = useRef(false); const opener = useRef<HTMLButtonElement | null>(null);
  const result = useRef<HTMLDivElement>(null); const heading = useRef<HTMLHeadingElement>(null); const summary = useRef<HTMLParagraphElement>(null);
  const pagingFocus = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => { if (success !== null || error !== null) result.current?.focus(); }, [success, error]);
  useEffect(() => { if (state.status === "ready" && pagingFocus.current) { pagingFocus.current = false; summary.current?.focus(); } }, [state]);
  const ready = state.status === "ready" ? state : null;
  // A write can finish after browser navigation; refresh the view that is visible now.
  const latestRead = useRef<{ reload: () => void; snapshot: Snapshot | null }>({ reload, snapshot: null });
  useEffect(() => { latestRead.current = { reload, snapshot: ready?.data ?? null }; }, [reload, ready]);
  const fresh = ready !== null && !ready.refreshing && ready.refreshError === null && ready.data !== stale;
  const locked = pending || !fresh || storageBlocked || attempt !== null;
  const selected = selection?.query === queryKey ? selection : null;
  const current = selected && ready?.data.list.items.find((item) => item.id === selected.payment.id);
  const selectionFresh = Boolean(selected && current && fresh && selected.snapshot === ready?.data && canChangePayment(current, selected.action));
  const problem = paymentProblem(error);
  const noteError = isHttpError(error) && error.status === 400 && Boolean(error.problem?.errors?.note);
  function navigate(query: UnappliedQuery) { setSelection(null); pagingFocus.current = true; setParameters(unappliedParameters(query)); }
  function closeSelection() {
    setSelection(null); window.requestAnimationFrame(() => { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); });
  }
  function clearAttempt() {
    try { sessionStorage.removeItem(storageKey); setAttempt(null); } catch { setStorageBlocked(true); }
  }
  async function execute(next: UnappliedAttempt) {
    if (!canManage || pending || storageBlocked) return false;
    let accepted = false;
    setSuccess(null);
    await run(async () => {
      try {
        try { sessionStorage.setItem(storageKey, JSON.stringify(next)); } catch { setStorageBlocked(true); throw new Error("Payment request could not be saved."); }
        if (mounted.current) setAttempt(next);
        await api.changeUnappliedPayment(storeId, next.id, next.action, next.key, next.note ?? undefined);
        accepted = true;
        if (mounted.current) { clearAttempt(); setSelection(null); setSuccess(next.action); }
      } catch (failure) {
        if (mounted.current) {
          if (uncertainPaymentWrite(failure)) setSelection(null);
          else clearAttempt();
        }
        throw failure;
      } finally {
        if (mounted.current) { setStale(latestRead.current.snapshot); latestRead.current.reload(); }
      }
    });
    return accepted;
  }
  return <div className="orders-page unapplied-page">
    <div className="order-page-heading"><div><h1 ref={heading} tabIndex={-1}>{t("title")}</h1><p className="hint">{t("intro")}</p></div>
      <Button type="button" variant="secondary" disabled={pending || state.status === "loading" || ready?.refreshing} onClick={reload}>{t("orders:refresh")}</Button></div>
    <div className="order-list-controls"><div className="order-sort"><label htmlFor="unapplied-status">{t("statusFilter")}</label>
      <select id="unapplied-status" value={status} disabled={pending} onChange={(event) => navigate({ page: 1, status: event.target.value as UnappliedQuery["status"] })}>
        <option value="">{t("allStatuses")}</option>{unappliedStatuses.map((value) => <option value={value} key={value}>{t(`orderDetails:unappliedStatus.${value}`)}</option>)}
      </select></div><p className="hint">{t("newestFirst")}</p></div>
    {invalid && <InlineMessage>{t("invalidQuery")} <Link to={`/stores/${encodeURIComponent(storeId)}/unapplied-payments`}>{t("orders:resetOptions")}</Link></InlineMessage>}
    <div ref={result} tabIndex={-1} className="order-action-result">
      {success && <InlineMessage tone="success">{t(`success.${success}`)}</InlineMessage>}
      {error !== null && <>{problem && <p>{t(`problems.${problem}`)}</p>}<RequestError error={error} operation="write" fieldErrors={noteError ? [{ field: "note", message: t("noteInvalid") }] : undefined} /><p className="hint">{t("writeFailure")}</p></>}
      {storageBlocked && <InlineMessage tone="error">{t("storageBlocked")}</InlineMessage>}
      {pending && <p role="status">{t("orders:saving")}</p>}
    </div>
    {canManage && attempt !== null && !pending && !storageBlocked && <UnappliedPaymentRecovery key={attempt.key} storeId={storeId} attempt={attempt} onRetry={execute}
      onFinish={() => { clearAttempt(); reload(); heading.current?.focus(); }} />}
    {state.status === "loading" && <LoadingState label={t("loading")} lines={4} />}
    {(state.status === "error" || state.status === "not-found") && <RequestError error={state.error} operation="read" onRetry={reload} />}
    {ready && <>
      <p ref={summary} tabIndex={-1} role="status">{t("orders:resultRange", {
        from: formatNumber(ready.data.list.items.length ? (page - 1) * ready.data.list.pageSize + 1 : 0, i18n.resolvedLanguage),
        to: formatNumber(ready.data.list.items.length ? (page - 1) * ready.data.list.pageSize + ready.data.list.items.length : 0, i18n.resolvedLanguage), total: formatNumber(ready.data.list.totalCount, i18n.resolvedLanguage),
      })}</p>
      {ready.refreshing && <p role="status">{t("orders:refreshing")}</p>}
      {ready.refreshError !== null && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
      {!fresh && <p role="status" className="hint">{t("stale")}</p>}
      {!canManage && <p className="hint">{t("orderDetails:readOnly")}</p>}
      {selected && <UnappliedPaymentConfirmation key={`${selected.payment.id}:${selected.action}`} payment={selected.payment} action={selected.action} pending={pending}
        disabled={locked || !selectionFresh} serverNoteError={noteError}
        onReview={fresh && current && !selectionFresh && !attempt && canChangePayment(current, selected.action) ? () => setSelection({ ...selected, payment: current, snapshot: ready.data }) : undefined}
        onConfirm={(note) => selectionFresh && !locked ? execute(newUnappliedAttempt(selected.payment, selected.action, note)) : Promise.resolve(false)} onClose={closeSelection} />}
      {ready.data.list.items.length === 0 ? <EmptyState headingLevel={2} title={t(page > 1 ? "outOfRange" : "emptyTitle")} action={page > 1 ? <Button type="button" variant="secondary" onClick={() => navigate({ page: 1, status })}>{t("orders:firstPageAction")}</Button> : undefined}>
        <p>{t(status ? "emptyFiltered" : "emptyAll")}</p>
      </EmptyState> : <ul className="unapplied-list" aria-label={t("title")} aria-busy={ready.refreshing}>
        {ready.data.list.items.map((payment) => <li className="order-panel" key={payment.id}>
          <h2><Link to={`/stores/${encodeURIComponent(storeId)}/orders/${encodeURIComponent(payment.orderNumber)}#order-unapplied-${encodeURIComponent(payment.id)}`}>{t("openOrder", { number: payment.orderNumber })}</Link></h2>
          <UnappliedPaymentFacts payment={payment} />
          {canManage && <div className="cluster">{(["refund", "close"] as const).filter((action) => canChangePayment(payment, action)).map((action) => <Button key={action} type="button" variant="secondary" disabled={locked || selected !== null}
            onClick={(event) => { opener.current = event.currentTarget; setSelection({ payment, action, snapshot: ready.data, query: queryKey }); }}>{t(`action.${action}`)}</Button>)}</div>}
          {payment.status === "Open" && !canChangePayment(payment, "refund") && <p className="hint">{t("externalRefund")}</p>}
        </li>)}
      </ul>}
      <nav className="order-pagination" aria-label={t("paging")}>
        <Button type="button" variant="secondary" disabled={page === 1 || pending || ready.refreshing} onClick={() => navigate({ page: page - 1, status })}>{t("orders:previous")}</Button>
        <span>{t("orders:pageSummary", { page: formatNumber(page, i18n.resolvedLanguage), pages: formatNumber(Math.max(1, Math.ceil(ready.data.list.totalCount / ready.data.list.pageSize)), i18n.resolvedLanguage) })}</span>
        <Button type="button" variant="secondary" disabled={!ready.data.list.hasMore || page >= maxUnappliedPage || pending || ready.refreshing} onClick={() => navigate({ page: page + 1, status })}>{t("orders:next")}</Button>
      </nav>
    </>}
  </div>;
}
