import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { Cart } from "../../cart";
import { formatPrice, useStore } from "../../storeContext";

export function CartSummary({ cart, pending }: { cart: Cart; pending: boolean }) {
  const { t } = useTranslation("cart");
  const store = useStore();

  return (
    <aside
      className={`cart-summary ${pending ? "updating" : ""}`}
      aria-labelledby="cart-summary-heading"
      aria-busy={pending || undefined}
    >
      <h2 id="cart-summary-heading">{t("summary")}</h2>
      <dl>
        <div>
          <dt>{t("itemCount", { count: cart.count })}</dt>
          <dd>{formatPrice(cart.itemsTotal, store)}</dd>
        </div>
        <div>
          <dt>{t("includedVat")}</dt>
          <dd>{formatPrice(cart.vatTotal, store)}</dd>
        </div>
      </dl>
      <p className="cart-total">
        <span>{t("total")}</span>
        <strong>{formatPrice(cart.itemsTotal, store)}</strong>
      </p>
      <p className="hint">{t("shippingAtCheckout")}</p>
      {pending && (
        <p className="cart-updating" role="status">
          {t("updatingCart")}
        </p>
      )}
      <Link
        to="/checkout"
        className="button cart-proceed"
        aria-disabled={pending || undefined}
        tabIndex={pending ? -1 : undefined}
        onClick={(event) => {
          if (pending) event.preventDefault();
        }}
      >
        {t("proceed")}
      </Link>
    </aside>
  );
}
