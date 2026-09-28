import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { getReturns, requestReturn } from "../account";
import { formatPrice, useStore } from "../storeContext";
import { useRequest } from "../useRequest";
import { formatDate } from "../utils/format";
import { RequestError } from "./ui/RequestError";

export function OrderReturns({
  number,
  onReturned,
}: {
  number: string;
  onReturned: () => void;
}) {
  const { t } = useTranslation("orders");
  const store = useStore();
  const [attempt, setAttempt] = useState(0);
  const [problem, setProblem] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  const answer = useRequest(`returns:${number}:${attempt}`, (signal) =>
    getReturns(number, signal),
  );
  async function send(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    lock.current = true;
    setProblem(null);
    setPending(true);
    const form = new FormData(event.currentTarget);
    const lines = (answer.status === "ready" ? answer.data.returnable : [])
      .map((line) => ({
        storeProductId: line.storeProductId,
        quantity: Number(form.get(line.storeProductId) ?? 0),
      }))
      .filter((line) => line.quantity > 0);
    try {
      await requestReturn(
        number,
        lines,
        String(form.get("reason")).trim() || null,
      );
      setAttempt((current) => current + 1);
      onReturned();
    } catch (error) {
      setProblem(error);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }
  if (answer.status === "error")
    return (
      <RequestError
        error={answer.error}
        operation="read"
        onRetry={answer.reload}
      />
    );
  if (answer.status !== "ready") return null;
  const { closesAt, returnable, returns } = answer.data;
  return (
    <section className="returns">
      <h2>{t("returnTitle")}</h2>
      {returns.length > 0 && (
        <ul className="return-list">
          {returns.map((sent) => (
            <li key={sent.number}>
              <span>
                {sent.number} ·{" "}
                {sent.lines
                  .map((line) => `${line.quantity} × ${line.productName}`)
                  .join(", ")}
              </span>
              <span className="hint">
                {sent.status === "Received"
                  ? t("returnStatus.Received", {
                      amount: formatPrice(sent.refundedAmount, store),
                    })
                  : t(`returnStatus.${knownReturnStatus(sent.status)}`)}
              </span>
            </li>
          ))}
        </ul>
      )}
      {returnable.length === 0 ? (
        <p className="hint">{t("returnNone")}</p>
      ) : (
        <form
          onSubmit={(event) => void send(event)}
          className="return-form"
          aria-busy={pending}
        >
          {closesAt && (
            <p className="hint">
              {t("returnUntil", { date: formatDate(closesAt, store.culture) })}
            </p>
          )}
          {returnable.map((line) => (
            <label key={line.storeProductId}>
              {line.productName}
              <select name={line.storeProductId} defaultValue="0">
                {Array.from({ length: line.quantity + 1 }, (_, quantity) => (
                  <option key={quantity} value={quantity}>
                    {quantity}
                  </option>
                ))}
              </select>
            </label>
          ))}
          <label>
            {t("returnReason")}{" "}
            <textarea name="reason" rows={2} maxLength={1000} />
          </label>
          <button type="submit" disabled={pending}>
            {t("returnAsk")}
          </button>
          {problem !== null && (
            <RequestError error={problem} operation="write" />
          )}
        </form>
      )}
    </section>
  );
}
function knownReturnStatus(
  status: string,
): "Requested" | "Accepted" | "Refused" | "unknown" {
  return ["Requested", "Accepted", "Refused"].includes(status)
    ? (status as "Requested" | "Accepted" | "Refused")
    : "unknown";
}
