import { useTranslation } from "react-i18next";
import { Link } from "react-router";

export function ShoppingProgress({ current }: { current: "cart" | "checkout" }) {
  const { t } = useTranslation(["cart", "checkout", "navigation"]);
  return (
    <nav className="shopping-progress" aria-label={t("checkout:shoppingProgress")}>
      <ol>
        <li aria-current={current === "cart" ? "step" : undefined}>
          <span aria-hidden="true">01</span>
          {current === "checkout" ? <Link to="/cart">{t("navigation:cart")}</Link> : t("navigation:cart")}
        </li>
        <li aria-current={current === "checkout" ? "step" : undefined}>
          <span aria-hidden="true">02</span>{t("checkout:title")}
        </li>
        <li><span aria-hidden="true">03</span>{t("checkout:confirmation")}</li>
      </ol>
    </nav>
  );
}
