import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { cartLineKey, type Cart } from "../cart";
import { useCart } from "../cartContext";
import { readCheckoutRecovery } from "../checkoutRecovery";
import { CartLine } from "../components/cart/CartLine";
import { CartSummary } from "../components/cart/CartSummary";
import { DiscountField } from "../components/DiscountField";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { ShoppingProgress } from "../components/ShoppingProgress";

export function CartPage() {
  const { t } = useTranslation(["cart", "catalog", "navigation"]);
  const {
    status,
    cart,
    error,
    pending,
    adjusted,
    acknowledgeAdjustment,
    reload,
  } = useCart();
  const [announcement, setAnnouncement] = useState("");
  const [checkoutRecovery] = useState(readCheckoutRecovery);
  const [focusTarget, setFocusTarget] = useState<string | null>(null);
  const focusedTarget = useRef<string | null>(null);

  useEffect(() => {
    if (!focusTarget || focusedTarget.current === focusTarget) return;
    focusedTarget.current = focusTarget;
    document.getElementById(focusTarget)?.focus();
  }, [cart, focusTarget]);

  function removed(updated: Cart, index: number, name: string) {
    const next = updated.items[Math.min(index, updated.items.length - 1)];
    focusedTarget.current = null;
    setFocusTarget(
      next ? `cart-line-link-${cartLineKey(next)}` : "cart-empty-heading",
    );
    setAnnouncement(t("cart:removedAnnouncement", { name }));
  }

  if (status === "error") {
    return (
      <section className="cart cart-load-state">
        <h1>{t("navigation:cart")}</h1>
        <RequestError error={error} operation="read" onRetry={reload} />
      </section>
    );
  }
  if (!cart) {
    return (
      <section className="cart cart-load-state">
        <h1>{t("navigation:cart")}</h1>
        <LoadingState label={t("cart:loading")} lines={4} />
      </section>
    );
  }

  return (
    <section className="cart" aria-labelledby="cart-heading">
      <ShoppingProgress current="cart" />
      <header className="shopping-page-heading">
        <h1 id="cart-heading">{t("navigation:cart")}</h1>
        <Link to="/">{t("cart:continueShopping")}</Link>
      </header>
      <p className="sr-only" aria-live="polite" aria-atomic="true">
        {announcement}
      </p>
      {checkoutRecovery && (
        <div className="cart-order-recovery notice" role="status">
          <div>
            <strong>{t("cart:orderRecoveryTitle")}</strong>
            <p>{t("cart:orderRecoveryBody")}</p>
          </div>
          <Link to={checkoutRecovery.orderPath} className="button">
            {t("cart:orderRecoveryAction")}
          </Link>
        </div>
      )}
      {error !== null && <RequestError error={error} operation="write" />}
      {adjusted && (
        <div className="cart-adjusted notice" role="status">
          <p>{t("cart:changed")}</p>
          <button
            type="button"
            className="link-button"
            onClick={acknowledgeAdjustment}
          >
            {t("cart:reviewed")}
          </button>
        </div>
      )}
      {cart.items.length === 0 ? (
        <div className="cart-empty" aria-labelledby="cart-empty-heading">
          <h2 id="cart-empty-heading" tabIndex={-1}>
            {t("cart:emptyTitle")}
          </h2>
          <p>{t("cart:emptyBody")}</p>
          <Link to="/" className="button">
            {t("catalog:browseAll")}
          </Link>
        </div>
      ) : (
        <div className="cart-layout">
          <div className="cart-items">
            <ul className="cart-lines" aria-label={t("cart:items")}>
              {cart.items.map((line, index) => (
                <CartLine
                  key={cartLineKey(line)}
                  line={line}
                  index={index}
                  onRemoved={removed}
                />
              ))}
            </ul>
            <DiscountField />
          </div>
          <CartSummary cart={cart} pending={pending} />
        </div>
      )}
    </section>
  );
}
