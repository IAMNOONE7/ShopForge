import { useTranslation } from "react-i18next";
import type { AdminOrderDetail } from "../api";
import { currencyFormatter, formatNumber } from "../utils/format";
import { InlineMessage } from "./ui/InlineMessage";
import "./OrderParcels.css";

export function OrderParcels({ order }: { order: AdminOrderDetail }) {
  const { t, i18n } = useTranslation("orderParcels");
  const money = currencyFormatter(order.currency, i18n.resolvedLanguage);
  return <section className="order-panel order-parcels" aria-labelledby="order-parcels-title">
    <h2 id="order-parcels-title" tabIndex={-1}>{t("title")}</h2>
    <p className="hint">{t("hint")}</p>
    {order.parcels === undefined ? <p>{t("notSupplied")}</p> : order.parcels === null
      ? <InlineMessage tone="error">{t("invalid")}</InlineMessage> : order.parcels.length === 0
        ? <p>{t("empty")}</p> : <>
          <p className="hint">{t("priceHint")}</p>
          <ol className="order-records">
            {order.parcels.map(parcel => <li className="order-record" key={parcel.number}>
              <div className="order-record-heading">
                <h3>{t("parcel", { number: formatNumber(parcel.number, i18n.resolvedLanguage) })}</h3>
                <span className="badge">{t(parcel.alone ? "alone" : "standard")}</span>
              </div>
              <dl className="order-facts">
                <div><dt>{t("grossWeight")}</dt><dd>{t("grams", { value: formatNumber(parcel.grossWeightGrams, i18n.resolvedLanguage) })}</dd></div>
                <div><dt>{t("band")}</dt><dd>{t("grams", { value: formatNumber(parcel.bandUpToGrams, i18n.resolvedLanguage) })}</dd></div>
                <div><dt>{t("price")}</dt><dd>{money.format(parcel.price)}</dd></div>
              </dl>
              <ul className="order-parcel-contents" aria-label={t("contents")}>
                {parcel.contents.map(content => <li key={content.variantId}>
                  <div><strong>{content.sku}</strong><span className="hint">{t("quantity", { value: formatNumber(content.quantity, i18n.resolvedLanguage) })}</span>
                    <details><summary>{t("variantId")}</summary><bdi>{content.variantId}</bdi></details>
                  </div>
                </li>)}
              </ul>
            </li>)}
          </ol>
        </>}
  </section>;
}
