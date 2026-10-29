import { useTranslation } from "react-i18next";
import { useStore } from "../../storeContext";
import { CurrencyValue } from "../CurrencyValue";

export function ShippingPrice({ price, fullPrice }: { price: number; fullPrice?: number }) {
  const { t } = useTranslation("checkout");
  const store = useStore();
  const reduced = fullPrice !== undefined && fullPrice > price;
  const money = (value: number) => <CurrencyValue value={value} culture={store.culture} currency={store.currency} />;
  return <span className="shipping-price">
    {reduced && <del><span className="sr-only">{t("shippingBeforeDiscount")}: </span>{money(fullPrice)}</del>}
    <span>{reduced && <span className="sr-only">{t("shippingCharged")}: </span>}{money(price)}</span>
  </span>;
}
