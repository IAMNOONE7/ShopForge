import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AdminUnappliedPayment } from "../api";
import type { UnappliedAction } from "../api/unappliedPayments";
import { currencyFormatter } from "../utils/format";
import { Button } from "./ui/Button";

type Props = {
  payment: AdminUnappliedPayment; action: UnappliedAction; disabled: boolean; pending: boolean;
  savedNote?: string; serverNoteError?: boolean; onReview?: () => void;
  onConfirm: (note: string) => Promise<boolean>; onClose: () => void;
};
export function UnappliedPaymentConfirmation({ payment, action, disabled, pending, savedNote, serverNoteError, onReview, onConfirm, onClose }: Props) {
  const { t, i18n } = useTranslation(["unapplied", "orderDetails", "orders"]);
  const [note, setNote] = useState(savedNote ?? "");
  const [verified, setVerified] = useState(false);
  const [invalid, setInvalid] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const noteField = useRef<HTMLTextAreaElement>(null);
  const verifiedField = useRef<HTMLInputElement>(null);
  useEffect(() => { heading.current?.focus(); }, [payment]);
  useEffect(() => {
    function escape(event: KeyboardEvent) { if (event.key === "Escape" && !pending) { event.preventDefault(); onClose(); } }
    document.addEventListener("keydown", escape); return () => document.removeEventListener("keydown", escape);
  }, [pending, onClose]);
  const noteInvalid = !note.trim() || note.trim().length > 500;
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (disabled) return;
    if (action === "close" && (noteInvalid || !verified)) {
      setInvalid(true); if (noteInvalid) noteField.current?.focus(); else verifiedField.current?.focus(); return;
    }
    await onConfirm(note.trim());
  }
  return <form className="order-confirmation" role="group" aria-labelledby="unapplied-confirm-title" noValidate onSubmit={(event) => void submit(event)} aria-busy={pending}>
    <h3 id="unapplied-confirm-title" ref={heading} tabIndex={-1}>{t(`confirmTitle.${action}`, { number: payment.orderNumber })}</h3>
    <p className="unapplied-identity">{currencyFormatter(payment.currency, i18n.resolvedLanguage).format(payment.amount)} · {payment.provider} · {payment.reference || t("orderDetails:noReference")}</p>
    <p>{t(`confirmHint.${action}`)}</p>
    {action === "close" && <>
      <div className="unapplied-note"><label htmlFor="unapplied-note">{t("note")}</label>
        <textarea ref={noteField} id="unapplied-note" name="note" rows={4} required maxLength={500} readOnly={savedNote !== undefined} disabled={pending}
          value={note} aria-invalid={(invalid && noteInvalid) || serverNoteError || undefined} aria-describedby="unapplied-note-hint unapplied-note-error"
          onChange={(event) => { setNote(event.target.value); setInvalid(false); setVerified(false); }} />
        <p id="unapplied-note-hint" className="hint">{t(savedNote !== undefined ? "savedNote" : "noteHint")}</p>
        <p id="unapplied-note-error" role={(invalid && noteInvalid) || serverNoteError ? "alert" : undefined}>{(invalid && noteInvalid) || serverNoteError ? t("noteInvalid") : ""}</p>
      </div>
      <div><label className="unapplied-verification"><input ref={verifiedField} id="unapplied-verified" type="checkbox" required checked={verified} disabled={pending}
        aria-invalid={invalid && !verified || undefined} aria-describedby={invalid && !verified ? "unapplied-verified-error" : undefined} onChange={(event) => setVerified(event.target.checked)} />{t("verified")}</label>
        {invalid && !verified && <p id="unapplied-verified-error" role="alert">{t("verificationRequired")}</p>}</div>
    </>}
    {disabled && !pending && <p role="status">{t("reviewRequired")}</p>}
    {onReview && <Button type="button" variant="secondary" onClick={() => { setVerified(false); onReview(); }}>{t("reviewUpdated")}</Button>}
    <div className="cluster"><Button type="submit" variant={action === "refund" ? "danger" : "primary"} busy={pending} busyLabel={t("orders:saving")} disabled={disabled}>{t(`confirmAction.${action}`)}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onClose}>{t("cancel")}</Button></div>
  </form>;
}
