import { useEffect, useRef, useState } from "react";
import { api, type AdminOrderReturn } from "./api";
import { HttpError, isHttpError } from "./api/http";
import type { ActionRunner } from "./useAction";
import { readRefundAttempt, refundNeedsRecovery, refundStorageKey, type RefundAttempt, type RefundOutcome } from "./utils/refundWorkflow";

export function useRefundAction({ storeId, userId, allowed, run, pending, onChanged }: {
  storeId: string; userId: string; allowed: boolean; run: ActionRunner; pending: boolean; onChanged: () => void;
}) {
  const storageKey = refundStorageKey(userId, storeId);
  const [saved] = useState(() => { try { return { attempt: allowed ? readRefundAttempt(storageKey) : null, blocked: false }; } catch { return { attempt: null, blocked: true }; } });
  const [attempt, setAttempt] = useState(saved.attempt);
  const [blocked, setBlocked] = useState(saved.blocked);
  const [outcome, setOutcome] = useState<RefundOutcome | null>(null);
  const [revision, setRevision] = useState(0);
  const mounted = useRef(false); const changed = useRef(onChanged);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => { changed.current = onChanged; }, [onChanged]);
  function clearAttempt() {
    try { sessionStorage.removeItem(storageKey); setAttempt(null); } catch { setBlocked(true); }
  }
  async function execute(next: RefundAttempt) {
    if (!allowed || pending || blocked || (attempt !== null && attempt.key !== next.key)) return;
    setOutcome(null);
    await run(async () => {
      try {
        try { sessionStorage.setItem(storageKey, JSON.stringify(next)); } catch { setBlocked(true); throw new Error("Refund request could not be saved."); }
        if (mounted.current) setAttempt(next);
        let records: AdminOrderReturn[];
        if (next.action === "order-refund") {
          const order = await api.refundOrder(storeId, next.orderNumber, next.key);
          if (order.number !== next.orderNumber || order.currency !== next.currency) throw new HttpError("invalid-response", 200);
          records = order.returns.filter((item) => !next.previousReturnIds.includes(item.id));
          if (!records.length || !records.some((item) => item.status === "Received" && item.refund !== null)) throw new HttpError("invalid-response", 200);
        } else {
          const record = await api.changeReturnMoney(storeId, next.returnId!, next.action, next.key, next.note ?? undefined);
          if (record.id !== next.returnId || record.orderNumber !== next.orderNumber || record.number !== next.returnNumber) throw new HttpError("invalid-response", 200);
          records = [record];
        }
        if (mounted.current) { clearAttempt(); setOutcome({ attempt: next, records }); setRevision((value) => value + 1); }
      } catch (error) {
        if (mounted.current) {
          if (!refundNeedsRecovery(error)) clearAttempt();
          else if (isHttpError(error) && error.status === 422) {
            const review = { ...next, reviewOnly: true as const };
            try { sessionStorage.setItem(storageKey, JSON.stringify(review)); setAttempt(review); } catch { setBlocked(true); }
          }
        }
        throw error;
      } finally {
        if (mounted.current) changed.current();
      }
    });
  }
  return { attempt, blocked, outcome, revision, execute, clearOutcome: () => setOutcome(null), finishReview: () => { clearAttempt(); changed.current(); } };
}
