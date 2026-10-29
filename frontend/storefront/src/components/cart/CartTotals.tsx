import { Trans, useTranslation } from "react-i18next";
import type { Cart, ShippingMethod } from "../../cart";
import { useStore } from "../../storeContext";
import { CurrencyValue } from "../CurrencyValue";
import { ShippingPrice } from "../checkout/ShippingPrice";

export function CartTotals({ cart, shipping }: { cart: Cart; shipping?: ShippingMethod }) {
  const { t } = useTranslation(["cart", "checkout"]);
  const store = useStore();
  const money = (value: number, prefix?: string) => <CurrencyValue value={value} culture={store.culture} currency={store.currency} prefix={prefix} />;
  const subtotal = cart.items.reduce((sum, line) => sum + line.lineTotal, 0);
  return (
    <>
      <dl className="shopping-totals">
        <div>
          <dt>{t("cart:subtotal", { count: cart.count })}</dt>
          <dd>{money(subtotal)}</dd>
        </div>
        {cart.discount && (
          <div className="shopping-discount">
            <dt>{t("cart:discount")} <bdi lang={store.culture}>{cart.discount.name}</bdi></dt>
            <dd>{cart.discount.amount > 0 ? money(cart.discount.amount, "−") : t("cart:codeApplied")}</dd>
          </div>
        )}
        {shipping && (
          <div>
            <dt>{t("checkout:shipping")} <bdi lang={store.culture}>{shipping.name}</bdi></dt>
            <dd><ShippingPrice price={shipping.price} fullPrice={shipping.fullPrice} /></dd>
          </div>
        )}
      </dl>
      <p className={shipping ? "checkout-total" : "cart-total"}>
        <span>{t(shipping ? "checkout:estimatedTotal" : "cart:total")}</span>
        <strong>{money(cart.itemsTotal + (shipping?.price ?? 0))}</strong>
      </p>
      {shipping ? (
        <p className="hint shopping-total-note">{t(cart.discount ? "checkout:discountEstimate" : "checkout:pricesIncludeVat")}</p>
      ) : (
        <p className="hint shopping-total-note"><Trans t={t} i18nKey="cart:taxAndShipping" components={{ tax: money(cart.vatTotal) }} /></p>
      )}
    </>
  );
}
