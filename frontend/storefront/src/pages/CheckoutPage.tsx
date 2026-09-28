import { useRef, useState, type FormEvent } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { statusOf } from "../api/errors";
import {
  getCart,
  getCheckoutMethods,
  getPickupPoints,
  placeOrder,
  type Cart,
  type CheckoutMethods,
} from "../cart";
import { useCart } from "../cartContext";
import { rememberCheckout } from "../checkoutRecovery";
import { AddressFields } from "../components/checkout/AddressFields";
import {
  emptyAddress,
  isAddressComplete,
  toAddress,
  type AddressDraft,
} from "../components/checkout/address";
import { CheckoutSummary } from "../components/checkout/CheckoutSummary";
import {
  PaymentMethodSelection,
  ShippingMethodSelection,
} from "../components/checkout/MethodSelection";
import { PickupPointSelect } from "../components/checkout/PickupPointSelect";
import { Message } from "../components/Message";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";
import type { Customer } from "../account";
import { useStore } from "../storeContext";
import { useRequest, type RequestState } from "../useRequest";

export function CheckoutPage() {
  const { t } = useTranslation(["checkout", "cart"]);
  const cartState = useCart();
  const customerState = useCustomer();
  const methods = useRequest("checkout-methods", getCheckoutMethods);

  if (cartState.status === "error") {
    return (
      <section className="checkout-load-state">
        <h1>{t("checkout:title")}</h1>
        <RequestError
          error={cartState.error}
          operation="read"
          onRetry={cartState.reload}
        />
      </section>
    );
  }

  if (!cartState.cart) {
    return (
      <section className="checkout-load-state">
        <h1>{t("checkout:title")}</h1>
        <LoadingState label={t("checkout:loadingCart")} lines={4} />
      </section>
    );
  }

  if (cartState.cart.items.length === 0) {
    return (
      <Message
        title={t("cart:emptyTitle")}
        text={t("cart:emptyCheckoutBody")}
      />
    );
  }

  if (customerState.status === "checking") {
    return (
      <section className="checkout-load-state">
        <h1>{t("checkout:title")}</h1>
        <LoadingState label={t("checkout:checkingCustomer")} lines={3} />
      </section>
    );
  }

  if (customerState.status === "error") {
    return (
      <section className="checkout-load-state">
        <h1>{t("checkout:title")}</h1>
        <RequestError
          error={customerState.error}
          operation="session"
          onRetry={customerState.retry}
        />
      </section>
    );
  }

  if (methods.status === "loading") {
    return (
      <section className="checkout-load-state">
        <h1>{t("checkout:title")}</h1>
        <LoadingState label={t("checkout:loadingMethods")} lines={4} />
      </section>
    );
  }

  if (methods.status === "error" || methods.status === "not-found") {
    return (
      <section className="checkout-load-state">
        <h1>{t("checkout:title")}</h1>
        <RequestError
          error={methods.error}
          operation="read"
          onRetry={methods.reload}
        />
      </section>
    );
  }

  const { paymentMethods, shippingMethods } = methods.data;
  if (paymentMethods.length === 0 || shippingMethods.length === 0) {
    const reason =
      paymentMethods.length === 0 && shippingMethods.length === 0
        ? t("checkout:unavailableStore")
        : paymentMethods.length === 0
          ? t("checkout:noPaymentMethods")
          : t("checkout:noShippingMethods");
    return (
      <Message title={t("checkout:unavailableTitle")} text={reason} />
    );
  }

  return (
    <CheckoutForm
      cart={cartState.cart}
      methods={methods.data}
      methodsRequest={methods}
      customer={customerState.customer}
      adjusted={cartState.adjusted}
      applyCart={cartState.apply}
      reloadCart={cartState.reload}
      acknowledgeAdjustment={cartState.acknowledgeAdjustment}
    />
  );
}

type CheckoutIssue = {
  target: string;
  message: string;
};

function CheckoutForm({
  cart,
  methods,
  methodsRequest,
  customer,
  adjusted,
  applyCart,
  reloadCart,
  acknowledgeAdjustment,
}: {
  cart: Cart;
  methods: CheckoutMethods;
  methodsRequest: Extract<
    RequestState<CheckoutMethods>,
    { status: "ready" }
  >;
  customer: Customer | null;
  adjusted: boolean;
  applyCart: (cart: Cart) => void;
  reloadCart: () => void;
  acknowledgeAdjustment: () => void;
}) {
  const { t } = useTranslation([
    "checkout",
    "auth",
    "cart",
    "validation",
  ]);
  const store = useStore();
  const navigate = useNavigate();
  const [email, setEmail] = useState(customer?.email ?? "");
  const [billing, setBilling] = useState<AddressDraft>(() => ({
    ...emptyAddress(),
    fullName: customer
      ? (customer.firstName + " " + customer.lastName).trim()
      : "",
  }));
  const [shipElsewhere, setShipElsewhere] = useState(false);
  const [shippingAddress, setShippingAddress] =
    useState<AddressDraft>(emptyAddress);
  const [shippingCode, setShippingCode] = useState("");
  const [paymentCode, setPaymentCode] = useState("");
  const [pickupPointCode, setPickupPointCode] = useState("");
  const [showValidation, setShowValidation] = useState(false);
  const [failed, setFailed] = useState<unknown | null>(null);
  const [refreshFailure, setRefreshFailure] = useState<unknown | null>(null);
  const [reviewRequired, setReviewRequired] = useState(false);
  const [refreshingCart, setRefreshingCart] = useState(false);
  const [pending, setPending] = useState(false);
  const submitLock = useRef(false);
  const validationSummary = useRef<HTMLDivElement>(null);
  const reviewNotice = useRef<HTMLDivElement>(null);

  const chosenShipping =
    methods.shippingMethods.find((method) => method.code === shippingCode) ??
    methods.shippingMethods[0];
  const chosenShippingCode = chosenShipping.code;
  const chosenPaymentCode =
    methods.paymentMethods.find((method) => method.code === paymentCode)?.code ??
    methods.paymentMethods[0].code;
  const pickupPoints = useRequest(
    "checkout-pickup:" +
      (chosenShipping.requiresPickupPoint ? chosenShipping.code : "none"),
    (signal) =>
      chosenShipping.requiresPickupPoint
        ? getPickupPoints(chosenShipping.code, signal)
        : Promise.resolve([]),
  );

  const issues = checkoutIssues({
    email: customer?.email ?? email,
    billing,
    shipElsewhere,
    shippingAddress,
    shippingMethodCode: chosenShippingCode,
    paymentMethodCode: chosenPaymentCode,
    pickupRequired: chosenShipping.requiresPickupPoint,
    pickupPointCode,
    pickupPoints,
    messages: {
      email: t("checkout:emailIssue"),
      billing: t("checkout:billingIssue"),
      shipping: t("checkout:shippingIssue"),
      shippingMethod: t("checkout:shippingMethodIssue"),
      paymentMethod: t("checkout:paymentMethodIssue"),
      pickup: t("checkout:pickupIssue"),
    },
  });
  const mustReview = reviewRequired || adjusted;
  const methodsRefreshing = methodsRequest.refreshing;
  const canSubmit =
    issues.length === 0 &&
    !mustReview &&
    !pending &&
    !refreshingCart &&
    !methodsRefreshing;

  function chooseShipping(code: string) {
    setShippingCode(code);
    setPickupPointCode("");
    setShowValidation(false);
  }

  async function refreshAfterConflict() {
    setRefreshingCart(true);
    setRefreshFailure(null);
    methodsRequest.reload();
    if (chosenShipping.requiresPickupPoint) pickupPoints.reload();
    try {
      applyCart(await getCart());
    } catch (error) {
      setRefreshFailure(error);
    } finally {
      setRefreshingCart(false);
    }
  }

  function reviewChanges() {
    acknowledgeAdjustment();
    setReviewRequired(false);
    setFailed(null);
    setRefreshFailure(null);
  }

  function focusTarget(target: string) {
    const field = document.querySelector<HTMLElement>(
      '[name="' + CSS.escape(target) + '"]',
    );
    field?.focus();
    field?.scrollIntoView?.({ block: "center", behavior: "smooth" });
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitLock.current || pending) return;

    if (mustReview) {
      reviewNotice.current?.focus();
      return;
    }

    if (issues.length > 0) {
      setShowValidation(true);
      window.requestAnimationFrame(() => validationSummary.current?.focus());
      return;
    }

    submitLock.current = true;
    setFailed(null);
    setRefreshFailure(null);
    setPending(true);
    try {
      const order = await placeOrder({
        email: (customer?.email ?? email).trim(),
        billingAddress: toAddress(billing),
        shippingAddress: shipElsewhere ? toAddress(shippingAddress) : null,
        paymentMethodCode: chosenPaymentCode,
        shippingMethodCode: chosenShippingCode,
        pickupPointCode: chosenShipping.requiresPickupPoint
          ? pickupPointCode
          : null,
      });
      const confirmationPath = rememberCheckout(order);
      reloadCart();
      if (order.redirectUrl) {
        window.location.assign(order.redirectUrl);
        return;
      }
      void navigate(confirmationPath, {
        state: { instructions: order.paymentInstructions },
      });
    } catch (error) {
      setFailed(error);
      if (statusOf(error) === 409) {
        setReviewRequired(true);
        await refreshAfterConflict();
      }
    } finally {
      setPending(false);
      submitLock.current = false;
    }
  }

  return (
    <form
      className="checkout"
      noValidate
      aria-busy={pending || undefined}
      onSubmit={(event) => void submit(event)}
    >
      <div className="checkout-fields">
        <h1>{t("checkout:title")}</h1>
        {methodsRequest.refreshError !== null && (
          <RequestError
            error={methodsRequest.refreshError}
            operation="read"
            onRetry={methodsRequest.reload}
          />
        )}
        {refreshFailure !== null && (
          <RequestError
            error={refreshFailure}
            operation="read"
            onRetry={() => void refreshAfterConflict()}
          />
        )}
        {mustReview && (
          <div
            className="checkout-review"
            ref={reviewNotice}
            role="alert"
            tabIndex={-1}
          >
            <InlineMessage title={t("checkout:reviewTitle")}>
              <p>{t("checkout:reviewBody")}</p>
              <button
                type="button"
                disabled={refreshingCart || methodsRefreshing}
                onClick={reviewChanges}
              >
                {refreshingCart || methodsRefreshing
                  ? t("checkout:refreshingOrder")
                  : t("checkout:reviewAction")}
              </button>
            </InlineMessage>
          </div>
        )}
        {showValidation && issues.length > 0 && (
          <div
            className="checkout-validation"
            ref={validationSummary}
            role="alert"
            tabIndex={-1}
          >
            <p>{t("checkout:validationSummary")}</p>
            <ul>
              {issues.map((issue) => (
                <li key={issue.target}>
                  <button
                    type="button"
                    className="link-button"
                    onClick={() => focusTarget(issue.target)}
                  >
                    {issue.message}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}
        {failed !== null && (
          <>
            <RequestError error={failed} operation="checkout" />
            {statusOf(failed) !== 409 && (
              <p className="hint">{t("checkout:noAutomaticRetry")}</p>
            )}
          </>
        )}

        <section
          className="checkout-section checkout-contact"
          aria-labelledby="checkout-contact-heading"
        >
          <h2 id="checkout-contact-heading">{t("checkout:contact")}</h2>
          <label>
            <span>{t("auth:email")}</span>
            <input
              name="email"
              type="email"
              autoComplete="email"
              value={customer?.email ?? email}
              readOnly={customer !== null}
              required
              aria-invalid={
                (showValidation &&
                  !isEmail(customer?.email ?? email)) ||
                undefined
              }
              onChange={(event) => setEmail(event.target.value)}
            />
          </label>
          {customer === null && (
            <p className="hint">
              <Trans
                ns="checkout"
                i18nKey="guestPrompt"
                components={{ signIn: <Link to="/account/sign-in" /> }}
              />
            </p>
          )}
        </section>

        <fieldset className="checkout-section">
          <legend>{t("checkout:billingAddress")}</legend>
          <AddressFields
            prefix="billing"
            value={billing}
            onChange={setBilling}
            showErrors={showValidation}
          />
        </fieldset>

        <label className="checkout-toggle">
          <input
            type="checkbox"
            checked={shipElsewhere}
            disabled={pending}
            onChange={(event) => {
              setShipElsewhere(event.target.checked);
              setShowValidation(false);
            }}
          />
          <span>{t("checkout:shipElsewhere")}</span>
        </label>

        {shipElsewhere && (
          <fieldset className="checkout-section">
            <legend>{t("checkout:shippingAddress")}</legend>
            <AddressFields
              prefix="shipping"
              value={shippingAddress}
              onChange={setShippingAddress}
              showErrors={showValidation}
            />
          </fieldset>
        )}

        <ShippingMethodSelection
          methods={methods.shippingMethods}
          selectedCode={chosenShippingCode}
          store={store}
          disabled={pending}
          onChange={chooseShipping}
        />

        {chosenShipping.requiresPickupPoint && (
          <PickupPointSelect
            points={pickupPoints}
            value={pickupPointCode}
            disabled={pending}
            showError={showValidation}
            onChange={setPickupPointCode}
          />
        )}

        <PaymentMethodSelection
          methods={methods.paymentMethods}
          selectedCode={chosenPaymentCode}
          disabled={pending}
          onChange={setPaymentCode}
        />
      </div>

      <CheckoutSummary
        cart={cart}
        shipping={chosenShipping}
        store={store}
        pending={pending}
      />

      <div className="checkout-actions">
        <button
          type="submit"
          className="button checkout-submit"
          disabled={pending || refreshingCart || methodsRefreshing}
          aria-disabled={!canSubmit || undefined}
          aria-describedby={
            !canSubmit && !pending && !mustReview
              ? "checkout-submit-hint"
              : undefined
          }
        >
          {pending
            ? t("checkout:placingOrder")
            : t("checkout:placeOrder")}
        </button>
        {!canSubmit && !pending && !mustReview && (
          <p id="checkout-submit-hint" className="hint">
            {t("checkout:completeOrder")}
          </p>
        )}
      </div>
    </form>
  );
}

function checkoutIssues({
  email,
  billing,
  shipElsewhere,
  shippingAddress,
  shippingMethodCode,
  paymentMethodCode,
  pickupRequired,
  pickupPointCode,
  pickupPoints,
  messages,
}: {
  email: string;
  billing: AddressDraft;
  shipElsewhere: boolean;
  shippingAddress: AddressDraft;
  shippingMethodCode: string;
  paymentMethodCode: string;
  pickupRequired: boolean;
  pickupPointCode: string;
  pickupPoints: RequestState<
    {
      code: string;
      name: string;
      line1: string;
      city: string;
      postalCode: string;
      country: string;
    }[]
  >;
  messages: {
    email: string;
    billing: string;
    shipping: string;
    shippingMethod: string;
    paymentMethod: string;
    pickup: string;
  };
}): CheckoutIssue[] {
  const issues: CheckoutIssue[] = [];
  if (!isEmail(email))
    issues.push({ target: "email", message: messages.email });
  if (!isAddressComplete(billing))
    issues.push({
      target: "billing.fullName",
      message: messages.billing,
    });
  if (shipElsewhere && !isAddressComplete(shippingAddress))
    issues.push({
      target: "shipping.fullName",
      message: messages.shipping,
    });
  if (!shippingMethodCode)
    issues.push({
      target: "shippingMethodCode",
      message: messages.shippingMethod,
    });
  if (!paymentMethodCode)
    issues.push({
      target: "paymentMethodCode",
      message: messages.paymentMethod,
    });
  if (pickupRequired) {
    const selectedExists =
      pickupPoints.status === "ready" &&
      pickupPoints.data.some((point) => point.code === pickupPointCode);
    if (!selectedExists)
      issues.push({
        target: "pickupPointCode",
        message: messages.pickup,
      });
  }
  return issues;
}

function isEmail(value: string) {
  const trimmed = value.trim();
  const parts = trimmed.split("@");
  return (
    trimmed === value &&
    trimmed.length <= 254 &&
    parts.length === 2 &&
    parts[0].length > 0 &&
    parts[1].length > 2 &&
    parts[1].includes(".")
  );
}
