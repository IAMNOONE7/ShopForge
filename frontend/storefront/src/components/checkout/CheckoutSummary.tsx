import { useTranslation } from "react-i18next";
import { cartLineKey, cartLineName, type Cart, type ShippingMethod } from "../../cart";
import type { Store } from "../../store";
import { formatPrice } from "../../storeContext";

export function CheckoutSummary({
  cart,
  shipping,
  store,
  pending,
}: {
  cart: Cart;
  shipping: ShippingMethod;
  store: Store;
  pending: boolean;
}) {
  const { t } = useTranslation(["checkout", "cart"]);
  const total = cart.itemsTotal + shipping.price;

  const breakdown = (
    <ul className="checkout-breakdown">
      {cart.items.map((line) => (
        <li key={cartLineKey(line)}>
          <span>
            {line.quantity} × <span lang={store.culture}>{cartLineName(line)}</span>
          </span>
          <span>{formatPrice(line.lineTotal, store)}</span>
        </li>
      ))}
      {cart.discount && (
        <li className="checkout-discount">
          <span>{cart.discount.name}</span>
          <span>−{formatPrice(cart.discount.amount, store)}</span>
        </li>
      )}
      <li>
        <span>{shipping.name}</span>
        <span>{formatPrice(shipping.price, store)}</span>
      </li>
    </ul>
  );

  return (
    <aside
      className="checkout-summary"
      aria-labelledby="checkout-summary-heading"
      aria-busy={pending || undefined}
    >
      <h2 id="checkout-summary-heading">{t("checkout:yourOrder")}</h2>
      <div className="checkout-items-desktop">{breakdown}</div>
      <details className="checkout-items-mobile">
        <summary>
          <span>{t("cart:itemCount", { count: cart.count })}</span>
          <span>{t("checkout:viewItems")}</span>
        </summary>
        {breakdown}
      </details>
      <div className="checkout-total">
        <span>{t("cart:total")}</span>
        <strong>{formatPrice(total, store)}</strong>
      </div>
      <p className="hint">{t("checkout:pricesIncludeVat")}</p>
      {pending && (
        <p className="checkout-submitting" role="status">
          {t("checkout:placingOrder")}
        </p>
      )}
    </aside>
  );
}
