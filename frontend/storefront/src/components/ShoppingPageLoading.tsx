import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { ShoppingProgress } from "./ShoppingProgress";
import { LoadingState } from "./ui/LoadingState";

export function ShoppingPageLoading({ kind, label, lines = 4 }: {
  kind: "cart" | "checkout";
  label: string;
  lines?: number;
}) {
  const { t } = useTranslation(["navigation", "checkout", "cart"]);
  return (
    <section className={kind === "cart" ? "cart cart-load-state" : "shopping-checkout-page checkout-load-state"} aria-busy="true">
      <ShoppingProgress current={kind} />
      <header className="shopping-page-heading">
        <h1>{t(kind === "cart" ? "navigation:cart" : "checkout:title")}</h1>
        <Link to={kind === "cart" ? "/" : "/cart"}>{t(kind === "cart" ? "cart:continueShopping" : "checkout:backToCart")}</Link>
      </header>
      {kind === "cart" ? <CartLoadingPreview label={label} /> : (
        <div className="checkout-loading-fields"><LoadingState label={label} lines={lines} /></div>
      )}
    </section>
  );
}

function CartLoadingPreview({ label }: { label: string }) {
  return (
    <div role="status" aria-live="polite">
      <span className="sr-only">{label}</span>
      <div className="cart-layout cart-loading-layout" aria-hidden="true">
        <div className="cart-items">
          <div className="cart-lines">
            <div className="cart-line cart-loading-line">
              <span className="cart-thumbnail cart-loading-block" />
              <div className="cart-line-identity"><span className="cart-loading-bar" /><span className="cart-loading-bar" /></div>
              <div className="cart-line-total"><span className="cart-loading-bar" /><span className="cart-loading-bar" /></div>
              <span className="cart-line-quantity cart-loading-control" />
              <span className="cart-remove cart-loading-control" />
            </div>
          </div>
          <div className="cart-loading-discount"><span className="cart-loading-bar" /><span className="cart-loading-control" /></div>
        </div>
        <div className="cart-summary cart-loading-summary">
          <span className="cart-loading-bar" /><span className="cart-loading-bar" />
          <span className="cart-loading-bar" /><span className="cart-loading-bar" />
          <span className="cart-loading-control" />
        </div>
      </div>
    </div>
  );
}
