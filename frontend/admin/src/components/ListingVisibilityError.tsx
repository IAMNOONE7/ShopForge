import { useTranslation } from "react-i18next";
import { Link } from "react-router";

export function ListingVisibilityError({ id, messages, productId }: { id: string; messages: string[]; productId?: string }) {
  const { t } = useTranslation("listings");
  if (!messages.length) return null;
  return <div id={id} className="listing-visibility-error" tabIndex={-1}>
    <p><strong>{t("visibilityRefused")}</strong></p>
    <ul>{messages.map(message => <li key={message}>{message}</li>)}</ul>
    <p>{t("visibilityMeasurements")}</p>
    <Link to={productId ? "/products/" + productId : "/products"}>{t("fixMeasurements")}</Link>
  </div>;
}
