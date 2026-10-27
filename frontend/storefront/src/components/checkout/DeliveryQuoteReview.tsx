import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { cartLineName, type Cart } from "../../cart";
import type { ShippingQuote, UnavailableShippingMethod } from "../../shippingQuotes";
import { formatPrice, useStore } from "../../storeContext";

export function DeliveryQuoteReview({ quote, previousPrice }: { quote: ShippingQuote; previousPrice: number }) {
  const { t } = useTranslation("checkout");
  const store = useStore();
  return <div className="delivery-quote-review">
    <p>{t("shippingPriceComparison", { previous: formatPrice(previousPrice, store), current: formatPrice(quote.total, store) })}</p>
    {quote.parcels.length > 0 && <ul className="delivery-parcel-prices">
      {quote.parcels.map((parcel, index) => <li key={index}>
        <span>{t("quotedParcel", { number: index + 1 })}</span> <strong>{formatPrice(parcel.price, store)}</strong>
      </li>)}
    </ul>}
    <p>{t("shippingPriceReviewHint")}</p>
  </div>;
}

export function UnavailableDeliveryMethods({ methods, cart }: { methods: UnavailableShippingMethod[]; cart: Cart }) {
  const { t } = useTranslation("checkout");
  const store = useStore();
  if (methods.length === 0) return null;
  return <section className="checkout-unavailable-delivery" aria-labelledby="unavailable-delivery-heading">
    <h3 id="unavailable-delivery-heading">{t("unavailableDeliveryHeading")}</h3>
    <ul>{methods.map(method => {
      const item = cart.items.find(line => line.variantId === method.variantId);
      return <li key={method.code}>
        <strong lang={store.culture}>{method.name}</strong>
        <p>{t(`shippingRefusals.${method.reason}`)}</p>
        {item && <p lang={store.culture}>{cartLineName(item)}</p>}
      </li>;
    })}</ul>
    <Link to="/cart">{t("editCart")}</Link>
  </section>;
}
