import { useTranslation } from "react-i18next";
import type { Cart, ShippingMethod } from "../../cart";
import { formatPrice, useStore } from "../../storeContext";

export function CartTotals({ cart, shipping }: { cart: Cart; shipping?: ShippingMethod }) {
  const { t } = useTranslation(["cart", "checkout"]);
  const store = useStore();
  const subtotal = cart.items.reduce((sum, line) => sum + line.lineTotal, 0);
  return (
    <>
      <dl className="shopping-totals">
        <div>
          <dt>{t("cart:subtotal", { count: cart.count })}</dt>
          <dd>{formatPrice(subtotal, store)}</dd>
        </div>
        {cart.discount && (
          <div className="shopping-discount">
            <dt>{t("cart:discount")} <bdi lang={store.culture}>{cart.discount.name}</bdi></dt>
            <dd>{cart.discount.amount > 0 ? "−" + formatPrice(cart.discount.amount, store) : t("cart:codeApplied")}</dd>
          </div>
        )}
        {shipping && (
          <div>
            <dt>{t("checkout:shipping")} <bdi lang={store.culture}>{shipping.name}</bdi></dt>
            <dd>{formatPrice(shipping.price, store)}</dd>
          </div>
        )}
      </dl>
      <p className={shipping ? "checkout-total" : "cart-total"}>
        <span>{t(shipping ? "checkout:estimatedTotal" : "cart:total")}</span>
        <strong>{formatPrice(cart.itemsTotal + (shipping?.price ?? 0), store)}</strong>
      </p>
      {shipping ? (
        <p className="hint shopping-total-note">{t(cart.discount ? "checkout:discountEstimate" : "checkout:pricesIncludeVat")}</p>
      ) : (
        <p className="hint shopping-total-note">{t("cart:taxAndShipping", { tax: formatPrice(cart.vatTotal, store) })}</p>
      )}
    </>
  );
}
