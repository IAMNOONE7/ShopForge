import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { getReturns, requestReturn, type ReturnableLine, type ReturnRequest } from "../account";
import { statusOf } from "../api/errors";
import { cartLineKey } from "../cart";
import { attemptFor, changedSince, inFlightWrite, uncertainWrite, type WriteAttempt } from "../idempotency";
import { useStore } from "../storeContext";
import { useRequest } from "../useRequest";
import { formatCurrency, formatDate } from "../utils/format";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

export function OrderReturns({
  number,
  orderStatus,
  currency,
  onReturned,
}: {
  number: string;
  orderStatus: string;
  currency: string;
  onReturned: () => void;
}) {
  const { t } = useTranslation("orders");
  const store = useStore();
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [reason, setReason] = useState("");
  const [problem, setProblem] = useState<unknown | null>(null);
  const [success, setSuccess] = useState(false);
  const [pending, setPending] = useState(false);
  const [reviewKind, setReviewKind] = useState<"conflict" | "inFlight" | "mismatch" | null>(null);
  const [attempt, setAttempt] = useState<WriteAttempt<ReturnRequest> | null>(null);
  const [now, setNow] = useState<number | null>(null);
  const lock = useRef(false);
  const reviewNotice = useRef<HTMLDivElement>(null);
  const answer = useRequest(`returns:${number}`, (signal) => getReturns(number, signal));

  useEffect(() => {
    const check = () => setNow(Date.now());
    check();
    const timer = window.setInterval(check, 60_000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    if (reviewKind !== null) reviewNotice.current?.focus();
  }, [reviewKind]);

  function selectedQuantity(line: ReturnableLine) {
    const value = quantities[cartLineKey(line)] ?? 0;
    return Number.isInteger(value) && value >= 0 && value <= line.quantity ? value : 0;
  }

  function bodyFor(returnable: ReturnableLine[]): ReturnRequest {
    return {
      lines: returnable
        .map((line) => ({
          storeProductId: line.storeProductId,
          variantId: line.variantId,
          quantity: selectedQuantity(line),
        }))
        .filter((line) => line.quantity > 0),
      reason: reason.trim() || null,
    };
  }

  async function send(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current || !canSend || answer.status !== "ready") {
      if (reviewKind !== null || changedAfterUncertain) reviewNotice.current?.focus();
      return;
    }
    const { closesAt, returnable } = answer.data;
    if (
      (orderStatus !== "Paid" && orderStatus !== "Shipped") ||
      (closesAt !== null && windowClosed)
    ) return;
    const request = bodyFor(returnable);
    if (request.lines.length === 0) return;

    lock.current = true;
    setProblem(null);
    setSuccess(false);
    setPending(true);
    try {
      const current = attemptFor(attempt, request);
      setAttempt(current);
      await requestReturn(number, current.payload, current.key);
      setAttempt(null);
      setQuantities({});
      setReason("");
      setSuccess(true);
      setReviewKind(null);
      answer.reload();
      onReturned();
    } catch (error) {
      setProblem(error);
      if (statusOf(error) === 409) {
        if (inFlightWrite(error)) {
          setReviewKind("inFlight");
        } else {
          setReviewKind("conflict");
          setQuantities({});
          answer.reload();
        }
      } else if (statusOf(error) === 422) {
        setReviewKind("mismatch");
      }
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  const data = answer.status === "ready" ? answer.data : null;
  const requestBody = data ? bodyFor(data.returnable) : null;
  const refreshing = answer.status === "ready" && answer.refreshing;
  const refreshError = answer.status === "ready" ? answer.refreshError : null;
  const windowClosed = data?.closesAt !== null && data?.closesAt !== undefined &&
    now !== null && Date.parse(data.closesAt) < now;
  const eligibleStatus = orderStatus === "Paid" || orderStatus === "Shipped";
  const canChoose = eligibleStatus && !windowClosed && (data?.returnable.length ?? 0) > 0;
  const hasSelection = (requestBody?.lines.length ?? 0) > 0;
  const changedAfterUncertain = problem !== null && uncertainWrite(problem) &&
    requestBody !== null && changedSince(attempt, requestBody);
  const canSend = canChoose && hasSelection && !pending && now !== null &&
    answer.status === "ready" && !refreshing && refreshError === null &&
    reviewKind === null && !changedAfterUncertain;

  function reviewChanges() {
    if (pending || refreshing || refreshError !== null) return;
    if (reviewKind !== "inFlight") setAttempt(null);
    setReviewKind(null);
    setProblem(null);
  }

  return (
    <section className="order-panel returns" aria-labelledby="returns-heading">
      <h2 id="returns-heading">{t("returnTitle")}</h2>
      {answer.status === "loading" && <LoadingState label={t("returnLoading")} lines={2} />}
      {(answer.status === "error" || answer.status === "not-found") && (
        <RequestError error={answer.error} operation="read" onRetry={answer.reload} />
      )}
      {data && (
        <>
          {data.returns.length > 0 && (
            <ul className="return-list">
              {data.returns.map((sent) => (
                <li key={sent.number}>
                  <span>
                    {sent.number} ·{" "}
                    <span lang={store.culture}>
                      {sent.lines.map((line) => `${line.quantity} × ${line.productName}`).join(", ")}
                    </span>
                  </span>
                  <span className="hint">
                    {sent.status === "Received"
                      ? t("returnStatus.Received", {
                          amount: formatCurrency(sent.refundedAmount, store.culture, currency),
                        })
                      : t(`returnStatus.${knownReturnStatus(sent.status)}`)}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {success && <InlineMessage tone="success">{t("returnRequested")}</InlineMessage>}
          {refreshError !== null && (
            <RequestError error={refreshError} operation="read" onRetry={answer.reload} />
          )}
          {problem !== null && <RequestError error={problem} operation="write" />}
          {(reviewKind !== null || changedAfterUncertain) && (
            <div className="return-review" ref={reviewNotice} role="alert" tabIndex={-1}>
              <InlineMessage title={t(reviewKind === "inFlight" ? "returnInFlightTitle" : reviewKind === "mismatch" ? "returnMismatchTitle" : changedAfterUncertain ? "returnChangedTitle" : "returnReviewTitle")}>
                <p>{t(reviewKind === "inFlight" ? "returnInFlightBody" : reviewKind === "mismatch" ? "returnMismatchBody" : changedAfterUncertain ? "returnChangedBody" : "returnReviewBody")}</p>
                <button type="button" disabled={pending || refreshing || refreshError !== null} onClick={reviewChanges}>
                  {t(reviewKind === "inFlight" ? "returnRetryReviewAction" : reviewKind === "conflict" ? "returnReviewAction" : "returnNewAttemptAction")}
                </button>
              </InlineMessage>
            </div>
          )}
          {problem !== null && uncertainWrite(problem) && !changedAfterUncertain && (
            <p className="hint">{t("returnSameRequestRetry")}</p>
          )}
          {data.returnable.length === 0 ? (
            <p className="hint">{t("returnNone")}</p>
          ) : !eligibleStatus ? (
            <p className="hint">{t("returnNotEligible")}</p>
          ) : windowClosed ? (
            <p className="hint">{t("returnWindowClosed")}</p>
          ) : (
            <form onSubmit={(event) => void send(event)} className="return-form" aria-busy={pending}>
              {data.closesAt && (
                <p className="hint">
                  {t("returnUntil", { date: formatDate(data.closesAt, store.culture) })}
                </p>
              )}
              {data.returnable.map((line) => {
                const identity = cartLineKey(line);
                return (
                  <label key={identity}>
                    <span lang={store.culture}>{line.productName}</span>
                    <select
                      value={selectedQuantity(line)}
                      disabled={pending || refreshing || refreshError !== null}
                      onChange={(event) => {
                        setQuantities((current) => ({ ...current, [identity]: Number(event.target.value) }));
                        setSuccess(false);
                      }}
                      aria-label={t("returnQuantity", { name: line.productName })}
                    >
                      {Array.from({ length: line.quantity + 1 }, (_, quantity) => (
                        <option key={quantity} value={quantity}>{quantity}</option>
                      ))}
                    </select>
                  </label>
                );
              })}
              <label className="return-reason">
                {t("returnReason")}
                <textarea
                  value={reason}
                  onChange={(event) => { setReason(event.target.value); setSuccess(false); }}
                  disabled={pending || refreshing || refreshError !== null}
                  rows={3}
                  maxLength={1000}
                />
              </label>
              <button type="submit" disabled={!canSend}>
                {pending ? t("returnSending") : t("returnAsk")}
              </button>
            </form>
          )}
        </>
      )}
    </section>
  );
}

function knownReturnStatus(status: string): "Requested" | "Accepted" | "Refused" | "unknown" {
  return ["Requested", "Accepted", "Refused"].includes(status)
    ? (status as "Requested" | "Accepted" | "Refused")
    : "unknown";
}
