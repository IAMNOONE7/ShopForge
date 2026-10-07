import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { cartLineKey, type Cart } from "../../cart";
import type { ShippingMethod } from "../../cart";
import type { Store } from "../../store";
import { formatPrice } from "../../storeContext";
import { productPath } from "../../publicPages";
import { CartTotals } from "../cart/CartTotals";

export function CheckoutSummary({ cart, shipping, store, pending, children }: {
  cart: Cart;
  shipping: ShippingMethod;
  store: Store;
  pending: boolean;
  children: ReactNode;
}) {
  const { t } = useTranslation(["checkout", "cart"]);
  return (
    <aside className="checkout-summary" aria-labelledby="checkout-summary-heading" aria-busy={pending || undefined}>
      <h2 id="checkout-summary-heading">{t("checkout:yourOrder")}</h2>
      <ul className="checkout-breakdown" aria-label={t("cart:items")}>
        {cart.items.map((line) => (
          <li key={cartLineKey(line)}>
            <div>
              <Link to={productPath(line.slug)} lang={store.culture}>{line.name}</Link>
              {line.optionValues.length > 0 && <bdi className="checkout-line-options" lang={store.culture}>{line.optionValues.join(" / ")}</bdi>}
              <span className="hint">{t("cart:quantityLabel")}: {line.quantity}</span>
            </div>
            <span>{formatPrice(line.lineTotal, store)}</span>
          </li>
        ))}
      </ul>
      <Link className="checkout-edit-cart" to="/cart">{t("checkout:editCart")}</Link>
      <CartTotals cart={cart} shipping={shipping} />
      {pending && <p className="checkout-submitting" role="status">{t("checkout:placingOrder")}</p>}
      {children}
    </aside>
  );
}
