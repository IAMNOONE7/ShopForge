import { useId } from "react";
import { Trans, useTranslation } from "react-i18next";
import { validFreeShipping, type FreeShipping } from "../../freeShipping";
import { useStore } from "../../storeContext";
import { CurrencyValue } from "../CurrencyValue";

export function FreeShippingProgress({ value, pending = false }: { value?: FreeShipping | null; pending?: boolean }) {
  const { t } = useTranslation("cart"), store = useStore(), heading = useId();
  if (!value || !validFreeShipping(value) || pending) return null;
  const reached = value.remaining === 0;
  const amount = (value: number) => <CurrencyValue value={value} culture={store.culture} currency={store.currency} />;
  return <section className="free-shipping-progress" aria-labelledby={heading}>
    <h3 id={heading}>{t(reached ? "freeShippingReached" : "freeShippingHeading")}</h3>
    <p aria-live="polite" aria-atomic="true"><Trans t={t} i18nKey={reached ? "freeShippingReachedBody" : "freeShippingRemaining"}
      components={{ remaining: amount(value.remaining), threshold: amount(value.threshold) }} /></p>
    <p className="hint">{t("freeShippingAvailability")}</p>
  </section>;
}
