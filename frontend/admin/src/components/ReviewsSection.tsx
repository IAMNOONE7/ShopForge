import { useTranslation } from "react-i18next";
import { api, type Review } from "../api";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
import { formatDate } from "../utils/format";
export function ReviewsSection({ storeId }: { storeId: string }) {
  const { t, i18n } = useTranslation(["reviews", "errors"]);
  const [reviews, reload] = useRequest(`reviews:${storeId}`, (signal) =>
    api.reviews(storeId, undefined, signal),
  );
  const [error, run] = useAction(reload);
  const all: Review[] = reviews.status === "ready" ? reviews.data : [];
  const waiting = all.filter((review) => review.status === "Pending");
  return (
    <section>
      <h2>{t("reviews:title")}</h2>
      <p className="hint">{t("reviews:hint")}</p>
      {reviews.status === "error" && (
        <RequestError error={reviews.error} operation="read" onRetry={reload} />
      )}
      {reviews.status === "ready" && reviews.refreshError !== null && (
        <RequestError
          error={reviews.refreshError}
          operation="read"
          onRetry={reload}
        />
      )}
      {error !== null && <RequestError error={error} operation="write" />}
      {reviews.status === "ready" && all.length === 0 && (
        <p className="hint">{t("reviews:empty")}</p>
      )}
      {all.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>{t("reviews:product")}</th>
              <th>{t("reviews:rating")}</th>
              <th>{t("reviews:review")}</th>
              <th>{t("reviews:written")}</th>
              <th>{t("reviews:status")}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {all.map((review) => (
              <tr key={review.id}>
                <td>{review.productName}</td>
                <td>{"★".repeat(review.rating)}</td>
                <td>
                  {review.text}
                  <span className="hint"> — {review.author}</span>
                </td>
                <td>{formatDate(review.writtenAt, i18n.resolvedLanguage)}</td>
                <td>
                  {t(`reviews:statuses.${knownReviewStatus(review.status)}`)}
                </td>
                <td className="inline-form compact">
                  {review.status !== "Published" && (
                    <button
                      type="button"
                      onClick={() =>
                        run(() => api.publishReview(storeId, review.id))
                      }
                    >
                      {t("reviews:publish")}
                    </button>
                  )}
                  {review.status !== "Rejected" && (
                    <button
                      type="button"
                      onClick={() =>
                        run(() => api.rejectReview(storeId, review.id))
                      }
                    >
                      {t("reviews:reject")}
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
          {t("reviews:waiting", { count: waiting.length })}
        </p>
      )}
    </section>
  );
}
function knownReviewStatus(
  status: string,
): "Pending" | "Published" | "Rejected" | "unknown" {
  return ["Pending", "Published", "Rejected"].includes(status)
    ? (status as "Pending" | "Published" | "Rejected")
    : "unknown";
}
