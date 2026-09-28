import { useTranslation } from "react-i18next";

export function Stars({ rating, count }: { rating: number; count?: number }) {
  const { t } = useTranslation("catalog");
  const shape = (
    <span aria-hidden="true">
      {"★".repeat(Math.round(rating)).padEnd(5, "☆")}
    </span>
  );
  if (count === undefined)
    return (
      <span className="stars" aria-label={t("rating", { rating })}>
        {shape}
      </span>
    );
  if (count === 0) return <span className="stars none">{t("noReviews")}</span>;
  return (
    <span
      className="stars"
      aria-label={t("ratingWithReviews", { rating, count })}
    >
      {shape} {rating.toFixed(1)} ({count})
    </span>
  );
}
