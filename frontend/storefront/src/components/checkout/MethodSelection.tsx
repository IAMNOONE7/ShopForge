import { useTranslation } from "react-i18next";
import type { PaymentMethod, ShippingMethod } from "../../cart";
import type { Store } from "../../store";
import { useStore } from "../../storeContext";
import { ShippingPrice } from "./ShippingPrice";

export function ShippingMethodSelection({
  methods,
  selectedCode,
  store,
  disabled,
  onChange,
  error,
}: {
  methods: ShippingMethod[];
  selectedCode: string;
  store: Store;
  disabled: boolean;
  onChange: (code: string) => void;
  error?: string[];
}) {
  const { t } = useTranslation("checkout");

  return (
    <fieldset className="checkout-section checkout-methods" aria-describedby={error?.length ? "shipping-method-error" : undefined}>
      <legend id="checkout-shipping-heading" tabIndex={-1}>{t("shipping")}</legend>
      {!!error?.length && <div id="shipping-method-error" role="alert" className="request-error">
        {error.map((message, index) => <p key={index}>{message}</p>)}
      </div>}
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
              aria-invalid={error?.length ? true : undefined}
              aria-describedby={error?.length ? "shipping-method-error" : undefined}
              onChange={() => onChange(method.code)}
            />
            <span className="checkout-option-copy">
              <span lang={store.culture}>{method.name}</span>
              <strong><ShippingPrice price={method.price} fullPrice={method.fullPrice} /></strong>
              {method.pricing === "perParcel" && <span className="hint">{t("parcelCount", { count: method.parcels ?? 0 })}</span>}
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
  const store = useStore();

  return (
    <fieldset className="checkout-section checkout-methods">
      <legend id="checkout-payment-heading" tabIndex={-1}>{t("payment")}</legend>
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
            <span lang={store.culture}>{method.name}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
