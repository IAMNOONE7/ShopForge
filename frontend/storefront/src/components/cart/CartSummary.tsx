import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { Cart } from "../../cart";
import { CartTotals } from "./CartTotals";

export function CartSummary({ cart, pending }: { cart: Cart; pending: boolean }) {
  const { t } = useTranslation("cart");

  return (
    <aside
      className={`cart-summary ${pending ? "updating" : ""}`}
      aria-labelledby="cart-summary-heading"
      aria-busy={pending || undefined}
    >
      <h2 id="cart-summary-heading">{t("summary")}</h2>
      <CartTotals cart={cart} />
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
