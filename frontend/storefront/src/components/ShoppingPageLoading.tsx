import { useTranslation } from "react-i18next";
import { LoadingState } from "./ui/LoadingState";

export function ShoppingPageLoading({ kind, label, lines = 4 }: {
  kind: "cart" | "checkout";
  label: string;
  lines?: number;
}) {
  const { t } = useTranslation(["navigation", "checkout"]);
  return (
    <section className={kind === "cart" ? "cart cart-load-state" : "checkout-load-state"} aria-busy="true">
      <h1>{t(kind === "cart" ? "navigation:cart" : "checkout:title")}</h1>
      <LoadingState label={label} lines={lines} />
    </section>
  );
}
