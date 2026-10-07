import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { writeReview } from "../account";
import { getReviews } from "../api";
import { useCustomer } from "../customerContext";
import { useStore } from "../storeContext";
import { useRequest } from "../useRequest";
import { formatDate } from "../utils/format";
import { Stars } from "./Stars";
import { RequestError } from "./ui/RequestError";

export function ProductReviews({ slug, reviewCount = 0 }: { slug: string; reviewCount?: number }) {
  const { t } = useTranslation(["catalog", "auth"]);
  const store = useStore();
  const { customer } = useCustomer();
  const [attempt, setAttempt] = useState(0);
  const [sent, setSent] = useState(false);
  const [problem, setProblem] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);
  const answer = useRequest(`reviews:${slug}:${attempt}`, (signal) =>
    getReviews(slug, signal),
  );

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;
    lock.current = true;
    setProblem(null);
    setPending(true);
    const form = new FormData(event.currentTarget);
    try {
      await writeReview(slug, {
        rating: Number(form.get("rating")),
        text: String(form.get("text")).trim(),
        author: customer
          ? `${customer.firstName} ${customer.lastName.slice(0, 1)}.`
          : t("auth:anonymousReviewAuthor"),
      });
      setSent(true);
      setAttempt((current) => current + 1);
    } catch (error) {
      setProblem(error);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  if (answer.status === "not-found") return null;
  if (answer.status === "loading" && reviewCount === 0 && !sent) return null;
  const canWrite = answer.status === "ready" && answer.data.canWrite;
  const reviews = answer.status === "ready" ? answer.data.reviews : [];
  if (answer.status === "ready" && reviews.length === 0 && !canWrite && !sent && reviewCount === 0) return null;

  return (
    <section className="reviews" aria-labelledby="product-reviews-heading">
      <h2 id="product-reviews-heading" tabIndex={-1}>{t("catalog:customerReviews")}</h2>
      {answer.status === "loading" && <p role="status">{t("catalog:loadingReviews")}</p>}
      {answer.status === "error" && (
        <RequestError error={answer.error} operation="read" onRetry={answer.reload} />
      )}
      {answer.status === "ready" && reviews.length === 0 && reviewCount > 0 && <p className="hint">{t("catalog:noReviews")}</p>}
      {reviews.map((review) => (
        <article
          key={`${review.author}-${review.writtenAt}`}
          className="review"
        >
          <p className="review-head">
            <Stars rating={review.rating} />
            <span className="hint">
              <bdi>{review.author}</bdi> · <time dateTime={review.writtenAt}>{formatDate(review.writtenAt, store.culture)}</time>
            </span>
          </p>
          <p className="review-text">{review.text}</p>
        </article>
      ))}
      {canWrite && !sent && (
        <form
          onSubmit={(event) => void submit(event)}
          className="review-form"
          aria-busy={pending}
        >
          <h3>{t("catalog:writeReview")}</h3>
          <p className="hint">{t("catalog:reviewEligibility")}</p>
          <label>
            {t("catalog:ratingLabel")}
            <select name="rating" defaultValue="5">
              {[5, 4, 3, 2, 1].map((value) => (
                <option key={value} value={value}>
                  {t("catalog:starCount", { count: value })}
                </option>
              ))}
            </select>
          </label>
          <label>
            {t("catalog:reviewLabel")}{" "}
            <textarea name="text" rows={3} maxLength={2000} required />
          </label>
          <button type="submit" disabled={pending}>
            {t("catalog:sendReview")}
          </button>
          {problem !== null && (
            <RequestError error={problem} operation="write" />
          )}
        </form>
      )}
      {sent && <p className="notice">{t("catalog:reviewSent")}</p>}
    </section>
  );
}
