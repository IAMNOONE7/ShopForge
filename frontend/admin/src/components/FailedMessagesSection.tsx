import { useTranslation } from "react-i18next";
import { api, type FailedMessage } from "../api";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
import { formatDateTime } from "../utils/format";
export function FailedMessagesSection({ storeId }: { storeId: string }) {
  const { t, i18n } = useTranslation(["operations", "common", "errors"]);
  const [messages, reload] = useRequest(
    `failed-messages:${storeId}`,
    (signal) => api.failedMessages(storeId, signal),
  );
  const [error, run] = useAction(reload);
  const failed: FailedMessage[] =
    messages.status === "ready" ? messages.data : [];
  return (
    <section>
      <h2>{t("operations:title")}</h2>
      <p className="hint">{t("operations:hint")}</p>
      {messages.status === "error" && (
        <RequestError
          error={messages.error}
          operation="read"
          onRetry={reload}
        />
      )}
      {messages.status === "ready" && messages.refreshError !== null && (
        <RequestError
          error={messages.refreshError}
          operation="read"
          onRetry={reload}
        />
      )}
      {error !== null && <RequestError error={error} operation="write" />}
      {messages.status === "ready" && failed.length === 0 && (
        <p className="hint">{t("operations:empty")}</p>
      )}
      {failed.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>{t("operations:message")}</th>
              <th>{t("operations:created")}</th>
              <th>{t("operations:attempts")}</th>
              <th>{t("operations:lastError")}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {failed.map((message) => (
              <tr key={message.id}>
                <td>{message.type}</td>
                <td>
                  {formatDateTime(message.createdAt, i18n.resolvedLanguage)}
                </td>
                <td>{message.attempts}</td>
                <td>
                  <details>
                    <summary>{t("operations:diagnostic")}</summary>
                    {message.error}
                  </details>
                </td>
                <td>
                  <button
                    type="button"
                    onClick={() =>
                      run(() => api.requeueMessage(storeId, message.id))
                    }
                  >
                    {t("common:tryAgain")}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
