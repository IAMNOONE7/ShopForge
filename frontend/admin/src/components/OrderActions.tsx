import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AdminOrderDetail } from "../api";
import { canChangeOrder, hasOpenGatewayPayment, type OrderAction } from "./orderDetailFacts";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

type Props = {
  order: AdminOrderDetail;
  disabled: boolean;
  pending: boolean;
  trackingError: boolean;
  onChange: (action: OrderAction, tracking: string) => Promise<void>;
  onFocusHeading: () => void;
};
export function OrderActions({ order, disabled, pending, trackingError, onChange, onFocusHeading }: Props) {
  const { t } = useTranslation(["orderDetails", "orders"]);
  const [tracking, setTracking] = useState("");
  const [invalid, setInvalid] = useState(false);
  const [confirmation, setConfirmation] = useState<{ kind: OrderAction; order: AdminOrderDetail; tracking: string } | null>(null);
  const opener = useRef<HTMLButtonElement | null>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);
  const mounted = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => { if (confirmation) confirmButton.current?.focus(); }, [confirmation]);
  useEffect(() => {
    if (!confirmation || pending) return;
    function escape(event: KeyboardEvent) { if (event.key === "Escape") { event.preventDefault(); close(); } }
    document.addEventListener("keydown", escape); return () => document.removeEventListener("keydown", escape);
  }, [confirmation, pending]); // eslint-disable-line react-hooks/exhaustive-deps
  const locked = disabled || confirmation !== null;
  const hasActions = (["payment", "cancel", "shipment"] as const).some((action) => canChangeOrder(order, action));
  const stale = confirmation !== null && (confirmation.order !== order || !canChangeOrder(order, confirmation.kind));
  function close() {
    setConfirmation(null);
    window.requestAnimationFrame(() => {
      if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else onFocusHeading();
    });
  }
  function open(kind: OrderAction, button: HTMLButtonElement | null) {
    if (locked || !canChangeOrder(order, kind)) return;
    if (kind === "shipment" && order.carrier === "manual" && !tracking.trim()) { setInvalid(true); document.getElementById("order-tracking")?.focus(); return; }
    opener.current = button; setConfirmation({ kind, order, tracking: tracking.trim() });
  }
  function ship(event: FormEvent<HTMLFormElement>) { event.preventDefault(); open("shipment", event.currentTarget.querySelector('button[type="submit"]')); }
  async function confirm() {
    if (!confirmation || disabled || stale) return;
    await onChange(confirmation.kind, confirmation.tracking);
    if (mounted.current) setConfirmation(null);
  }
  return <section className="order-panel order-actions" aria-labelledby="order-actions-title">
    <h2 id="order-actions-title" tabIndex={-1}>{t("orders:actions")}</h2>
    {!hasActions && <p className="hint">{t("noActions")}</p>}
    {pending && <p role="status">{t("orders:saving")}</p>}
    <div className="cluster">
      {canChangeOrder(order, "payment") && <Button type="button" disabled={locked} onClick={(event) => open("payment", event.currentTarget)}>{t("orders:markPaid")}</Button>}
      {canChangeOrder(order, "cancel") && <Button type="button" variant="secondary" disabled={locked} onClick={(event) => open("cancel", event.currentTarget)}>{t("cancelOrder")}</Button>}
    </div>
    {order.status === "AwaitingPayment" && hasOpenGatewayPayment(order) && <p className="hint">{t("orders:gatewayManaged")} <a href="#order-payment-attempts">{t("reviewAttempts")}</a></p>}
    {canChangeOrder(order, "shipment") && <form className="order-shipment-form" noValidate onSubmit={ship}>
      <Field id="order-tracking" name="trackingNumber" label={t("orders:trackingNumber")} hint={t(order.carrier === "manual" ? "trackingManual" : "trackingOptional")}
        required={order.carrier === "manual"} value={tracking} disabled={locked} error={invalid && order.carrier === "manual" ? t("trackingRequired") : trackingError ? t("trackingInvalid") : undefined} onChange={(event) => { setTracking(event.target.value); setInvalid(false); }} />
      <Button type="submit" disabled={locked}>{t("orders:ship")}</Button>
    </form>}
    {confirmation && <div className="order-confirmation" role="group" aria-label={t(`confirmTitle.${confirmation.kind}`, { number: order.number })}>
      <h3>{t(`confirmTitle.${confirmation.kind}`, { number: order.number })}</h3>
      <p>{t(`confirmHint.${confirmation.kind}`)}</p>
      {confirmation.kind === "shipment" && <p>{t("shipmentConfirmation", { carrier: order.carrier ?? t("notRecorded"), tracking: confirmation.tracking || t("notRecorded") })}</p>}
      {stale && <p role="status">{t("confirmationStale")}</p>}
      <div className="cluster"><Button ref={confirmButton} type="button" variant={confirmation.kind === "cancel" ? "danger" : "primary"}
        busy={pending} busyLabel={t("orders:saving")} disabled={disabled || stale} onClick={() => void confirm()}>{t(`confirmAction.${confirmation.kind}`)}</Button>
        <Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("keepOrder")}</Button></div>
    </div>}
  </section>;
}
