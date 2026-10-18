import { useTranslation } from "react-i18next";
import type { OrderReturn } from "../api";
import { formatNumber } from "../utils/format";

export function ReturnLines({ lines }: { lines: OrderReturn["lines"] }) {
  const { t, i18n } = useTranslation("returnReview");
  if (lines.length === 0) return <p>{t("noLines")}</p>;
  return <ol className="return-lines">{lines.map((line, index) => <li key={`${index}:${line.storeProductId}:${line.variantId}`}>
    <span className="hint">{t("lineNumber", { number: formatNumber(index + 1, i18n.resolvedLanguage) })}</span>
    <p>{line.productName}</p><p>{t("quantity", { quantity: formatNumber(line.quantity, i18n.resolvedLanguage) })}</p>
    <dl className="return-line-identities">
      <div><dt>{t("listingId")}</dt><dd><bdi dir="ltr"><code>{line.storeProductId}</code></bdi></dd></div>
      <div><dt>{t("variantId")}</dt><dd><bdi dir="ltr"><code>{line.variantId}</code></bdi></dd></div>
    </dl>
  </li>)}</ol>;
}
