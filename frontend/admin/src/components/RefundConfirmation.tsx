import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AdminOrderDetail, OrderReturn } from "../api";
import { currencyFormatter } from "../utils/format";
import type { RefundAction } from "../utils/refundWorkflow";
import { Button } from "./ui/Button";
import { ReturnLines } from "./ReturnLines";

export function RefundConfirmation({ action, order, record, disabled, pending, savedNote, noteError, onConfirm, onClose, onReview }: {
  action: RefundAction; order: AdminOrderDetail; record?: OrderReturn | null; disabled: boolean; pending: boolean;
  savedNote?: string; noteError?: boolean; onConfirm: (note: string) => Promise<void>; onClose: () => void; onReview?: () => void;
}) {
  const { t, i18n } = useTranslation(["refunds", "orders", "orderDetails"]);
  const [note, setNote] = useState(savedNote ?? ""); const [verified, setVerified] = useState(false); const [invalid, setInvalid] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null); const noteField = useRef<HTMLTextAreaElement>(null); const verifyField = useRef<HTMLInputElement>(null);
  useEffect(() => { heading.current?.focus(); }, [action]);
  useEffect(() => {
    function escape(event: KeyboardEvent) { if (event.key === "Escape" && !pending) { event.preventDefault(); onClose(); } }
    document.addEventListener("keydown", escape); return () => document.removeEventListener("keydown", escape);
  }, [pending, onClose]);
  const needsVerification = action !== "retry";
  const invalidNote = action === "close" && (!note.trim() || note.trim().length > 500);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (disabled) return;
    if (invalidNote || (needsVerification && !verified)) { setInvalid(true); if (invalidNote) noteField.current?.focus(); else verifyField.current?.focus(); return; }
    await onConfirm(note.trim());
  }
  return <form className="order-confirmation refund-confirmation" role="group" aria-labelledby="refund-confirm-title" aria-busy={pending} noValidate onSubmit={(event) => void submit(event)}>
    <h3 ref={heading} id="refund-confirm-title" tabIndex={-1}>{t(`confirmTitle.${action}`, { number: record?.number ?? order.number })}</h3>
    <p>{t(`confirmHint.${action}`)}</p>
    {(action === "retry" || action === "close") && record && <p>{t("orderDetails:creditedAmount")}: {currencyFormatter(order.currency, i18n.resolvedLanguage).format(record.refundedAmount)}</p>}
    {action === "receive" && record && <ReturnLines lines={record.lines} />}
    {action === "order-refund" && <p className="hint">{t("remainingHint")}</p>}
    {action === "close" && <div className="unapplied-note"><label htmlFor="refund-note">{t("note")}</label>
      <textarea ref={noteField} id="refund-note" name="refund-note" rows={4} maxLength={500} required value={note} readOnly={savedNote !== undefined} disabled={pending}
        aria-describedby="refund-note-hint refund-note-error" aria-invalid={(invalid && invalidNote) || noteError || undefined}
        onChange={(event) => { setNote(event.target.value); setInvalid(false); setVerified(false); }} />
      <p id="refund-note-hint" className="hint">{t(savedNote !== undefined ? "savedNote" : "noteHint")}</p>
      <p id="refund-note-error" role={(invalid && invalidNote) || noteError ? "alert" : undefined}>{(invalid && invalidNote) || noteError ? t("noteInvalid") : ""}</p>
    </div>}
    {needsVerification && <div><label className="unapplied-verification"><input ref={verifyField} id="refund-verified" type="checkbox" checked={verified} disabled={pending} required
      aria-invalid={invalid && !verified || undefined} aria-describedby={invalid && !verified ? "refund-verification-error" : undefined} onChange={(event) => setVerified(event.target.checked)} />{t(`verify.${action}`)}</label>
      {invalid && !verified && <p id="refund-verification-error" role="alert">{t("verificationRequired")}</p>}</div>}
    {disabled && !pending && <p role="status">{t("reviewRequired")}</p>}
    {onReview && <Button type="button" variant="secondary" onClick={() => { setVerified(false); onReview(); }}>{t("reviewCurrent")}</Button>}
    <div className="cluster"><Button type="submit" variant={action === "close" ? "primary" : "danger"} busy={pending} busyLabel={t("orders:saving")} disabled={disabled}>{t(`confirmAction.${action}`)}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onClose}>{t("cancel")}</Button></div>
  </form>;
}
