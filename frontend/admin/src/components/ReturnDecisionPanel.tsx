import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { OrderReturn } from "../api";
import type { ReturnDecision } from "../api/returns";
import { canDecideReturn } from "../utils/returnDecision";
import { Button } from "./ui/Button";

export function ReturnDecisionPanel({ record, snapshot, disabled, pending, onDecide }: {
  record: OrderReturn; snapshot: object; disabled: boolean; pending: boolean; onDecide: (decision: ReturnDecision) => Promise<void>;
}) {
  const { t } = useTranslation(["returnReview", "returns", "orders"]);
  const [draft, setDraft] = useState<{ decision: ReturnDecision; snapshot: object } | null>(null);
  const opener = useRef<HTMLButtonElement | null>(null);
  const heading = useRef<HTMLHeadingElement>(null); const confirmation = useRef<HTMLHeadingElement>(null);
  const stale = draft !== null && (draft.snapshot !== snapshot || !canDecideReturn(record, draft.decision));
  useEffect(() => { if (draft) confirmation.current?.focus(); }, [draft]);
  function close() {
    setDraft(null); window.requestAnimationFrame(() => { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); });
  }
  useEffect(() => {
    function escape(event: KeyboardEvent) { if (draft && !pending && event.key === "Escape") { event.preventDefault(); close(); } }
    document.addEventListener("keydown", escape); return () => document.removeEventListener("keydown", escape);
  }, [draft, pending]); // eslint-disable-line react-hooks/exhaustive-deps
  return <section className="order-panel return-decisions" aria-labelledby="return-actions-title">
    <h2 id="return-actions-title" ref={heading} tabIndex={-1}>{t("actionsTitle")}</h2>
    <div className="cluster">{(["accept", "refuse"] as const).filter((decision) => canDecideReturn(record, decision)).map((decision) => <Button key={decision} type="button" variant="secondary" disabled={disabled || draft !== null}
      onClick={(event) => { opener.current = event.currentTarget; setDraft({ decision, snapshot }); }}>{t(`returns:${decision}`)}</Button>)}</div>
    {!canDecideReturn(record, "accept") && !canDecideReturn(record, "refuse") && <p className="hint">{t("noDecisions")}</p>}
    {draft && <div className="order-confirmation" role="group" aria-labelledby="return-confirm-title">
      <h3 ref={confirmation} id="return-confirm-title" tabIndex={-1}>{t(`confirmTitle.${draft.decision}`, { number: record.number })}</h3>
      <p>{t(`confirmHint.${draft.decision}`)}</p>
      {stale && <p role="status">{t("confirmationStale")}</p>}
      {stale && !disabled && canDecideReturn(record, draft.decision) && <Button type="button" variant="secondary" onClick={() => setDraft({ ...draft, snapshot })}>{t("reviewUpdated")}</Button>}
      <div className="cluster"><Button type="button" variant={draft.decision === "refuse" ? "danger" : "primary"} disabled={disabled || stale} busy={pending} busyLabel={t("orders:saving")}
        onClick={() => { if (!disabled && !stale) void onDecide(draft.decision); }}>{t(`confirmAction.${draft.decision}`)}</Button>
        <Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancelDecision")}</Button></div>
    </div>}
  </section>;
}
