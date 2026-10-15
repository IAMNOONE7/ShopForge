import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { AdminOrderDetail, OrderReturn } from "../api";
import { canChangeRefund, makeRefundAttempt, type RefundAction, type RefundAttempt } from "../utils/refundWorkflow";
import { RefundConfirmation } from "./RefundConfirmation";
import { Button } from "./ui/Button";

export function RefundActions({ target, order, record, snapshot, disabled, pending, noteError, onExecute }: {
  target: "order" | "return"; order: AdminOrderDetail; record?: OrderReturn | null; snapshot: object;
  disabled: boolean; pending: boolean; noteError?: boolean; onExecute: (attempt: RefundAttempt) => Promise<void>;
}) {
  const { t } = useTranslation("refunds");
  const [draft, setDraft] = useState<{ action: RefundAction; snapshot: object } | null>(null);
  const opener = useRef<HTMLButtonElement | null>(null); const heading = useRef<HTMLHeadingElement>(null);
  const actions: RefundAction[] = target === "order" ? ["order-refund"] : ["receive", "retry", "close"];
  const available = actions.filter((action) => canChangeRefund(action, order, record));
  const stale = draft !== null && (draft.snapshot !== snapshot || !canChangeRefund(draft.action, order, record));
  function close() {
    setDraft(null); window.requestAnimationFrame(() => { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); });
  }
  return <section className="order-panel refund-actions" aria-labelledby={`${target}-refund-actions`}>
    <h2 id={`${target}-refund-actions`} ref={heading} tabIndex={-1}>{t(target === "order" ? "orderTitle" : "returnTitle")}</h2>
    <p className="hint">{t("intro")}</p>
    {available.length > 0 ? <div className="cluster">{available.map((action) => <Button key={action} type="button" variant="secondary" disabled={disabled || draft !== null}
      onClick={(event) => { opener.current = event.currentTarget; setDraft({ action, snapshot }); }}>{t(`actions.${action}`)}</Button>)}</div> : <p>{t(record?.refund === "ByStore" ? "byStore" : "noActions")}</p>}
    {draft && <RefundConfirmation key={draft.action} action={draft.action} order={order} record={record} disabled={disabled || stale} pending={pending} noteError={noteError}
      onReview={!disabled && stale && canChangeRefund(draft.action, order, record) ? () => setDraft({ ...draft, snapshot }) : undefined}
      onConfirm={(note) => !disabled && !stale ? onExecute(makeRefundAttempt(draft.action, order, record ?? undefined, note)) : Promise.resolve()}
      onClose={close} />}
  </section>;
}
