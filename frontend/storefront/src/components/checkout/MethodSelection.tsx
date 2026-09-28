import { useTranslation } from "react-i18next";
import type { PaymentMethod, ShippingMethod } from "../../cart";
import type { Store } from "../../store";
import { formatPrice } from "../../storeContext";

export function ShippingMethodSelection({
  methods,
  selectedCode,
  store,
  disabled,
  onChange,
}: {
  methods: ShippingMethod[];
  selectedCode: string;
  store: Store;
  disabled: boolean;
  onChange: (code: string) => void;
}) {
  const { t } = useTranslation("checkout");

  return (
    <fieldset className="checkout-section checkout-methods">
      <legend>{t("shipping")}</legend>
      <div className="checkout-options">
        {methods.map((method) => (
          <label key={method.code} className="checkout-option">
            <input
              type="radio"
              name="shippingMethodCode"
              value={method.code}
              checked={selectedCode === method.code}
              disabled={disabled}
              required
              onChange={() => onChange(method.code)}
            />
            <span className="checkout-option-copy">
              <span>{method.name}</span>
              <strong>{formatPrice(method.price, store)}</strong>
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

export function PaymentMethodSelection({
  methods,
  selectedCode,
  disabled,
  onChange,
}: {
  methods: PaymentMethod[];
  selectedCode: string;
  disabled: boolean;
  onChange: (code: string) => void;
}) {
  const { t } = useTranslation("checkout");

  return (
    <fieldset className="checkout-section checkout-methods">
      <legend>{t("payment")}</legend>
      <div className="checkout-options">
        {methods.map((method) => (
          <label key={method.code} className="checkout-option">
            <input
              type="radio"
              name="paymentMethodCode"
              value={method.code}
              checked={selectedCode === method.code}
              disabled={disabled}
              required
              onChange={() => onChange(method.code)}
            />
            <span>{method.name}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
