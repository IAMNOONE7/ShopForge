import { useTranslation } from "react-i18next";
import { api, type OrderReturn } from "../api";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
import { formatDate } from "../utils/format";
export function ReturnsSection({
  storeId,
  money,
}: {
  storeId: string;
  money: Intl.NumberFormat;
}) {
  const { t, i18n } = useTranslation(["returns", "errors"]);
  const [returns, reload] = useRequest(`returns:${storeId}`, (signal) =>
    api.returns(storeId, signal),
  );
  const [error, run] = useAction(reload);
  const all: OrderReturn[] = returns.status === "ready" ? returns.data : [];
  const waiting = all.filter(
    (sent) => sent.status === "Requested" || sent.status === "Accepted",
  );
  return (
    <section>
      <h2>{t("returns:title")}</h2>
      <p className="hint">{t("returns:hint")}</p>
      {returns.status === "error" && (
        <RequestError error={returns.error} operation="read" onRetry={reload} />
      )}
      {returns.status === "ready" && returns.refreshError !== null && (
        <RequestError
          error={returns.refreshError}
          operation="read"
          onRetry={reload}
        />
      )}
      {error !== null && <RequestError error={error} operation="write" />}
      {returns.status === "ready" && all.length === 0 && (
        <p className="hint">{t("returns:empty")}</p>
      )}
      {all.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>{t("returns:returnNumber")}</th>
              <th>{t("returns:order")}</th>
              <th>{t("returns:items")}</th>
              <th>{t("returns:requested")}</th>
              <th>{t("returns:status")}</th>
              <th>{t("returns:refunded")}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {all.map((sent) => (
              <tr key={sent.id}>
                <td>{sent.number}</td>
                <td>{sent.orderNumber}</td>
                <td>
                  {sent.lines
                    .map((line) => `${line.quantity} × ${line.productName}`)
                    .join(", ")}
                  {sent.reason && (
                    <span className="hint"> — {sent.reason}</span>
                  )}
                </td>
                <td>{formatDate(sent.requestedAt, i18n.resolvedLanguage)}</td>
                <td>
                  {t(`returns:statuses.${knownReturnStatus(sent.status)}`)}
                </td>
                <td>
                  {sent.status === "Received"
                    ? money.format(sent.refundedAmount)
                    : ""}
                </td>
                <td className="inline-form compact">
                  {sent.status === "Requested" && (
                    <button
                      type="button"
                      onClick={() =>
                        run(() => api.decideReturn(storeId, sent.id, "accept"))
                      }
                    >
                      {t("returns:accept")}
                    </button>
                  )}
                  {sent.status === "Accepted" && (
                    <button
                      type="button"
                      onClick={() =>
                        run(() => api.decideReturn(storeId, sent.id, "receive"))
                      }
                    >
                      {t("returns:received")}
                    </button>
                  )}
                  {(sent.status === "Requested" ||
                    sent.status === "Accepted") && (
                    <button
                      type="button"
                      onClick={() =>
                        run(() => api.decideReturn(storeId, sent.id, "refuse"))
                      }
                    >
                      {t("returns:refuse")}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {waiting.length > 0 && (
        <p className="hint">
          {t("returns:waiting", { count: waiting.length })}
        </p>
      )}
    </section>
  );
}
function knownReturnStatus(
  status: string,
): "Requested" | "Accepted" | "Refused" | "Received" | "unknown" {
  return ["Requested", "Accepted", "Refused", "Received"].includes(status)
    ? (status as "Requested" | "Accepted" | "Refused" | "Received")
    : "unknown";
}
