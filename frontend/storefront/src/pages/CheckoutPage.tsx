import { useEffect, useRef, useState, type FormEvent } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { statusOf } from "../api/errors";
import { HttpError } from "../api/http";
import {
  getCart,
  getCheckoutMethods,
  getPickupPoints,
  placeOrder,
  type Cart,
  type CheckoutRequest,
} from "../cart";
import { useCart } from "../cartContext";
import { rememberCheckout } from "../checkoutRecovery";
import { AddressFields } from "../components/checkout/AddressFields";
import {
  emptyAddress,
  isAddressComplete,
  isCountryCode,
  toAddress,
  type AddressDraft,
} from "../components/checkout/address";
import { CheckoutSummary } from "../components/checkout/CheckoutSummary";
import { CheckoutReview } from "../components/checkout/CheckoutReview";
import { ShoppingProgress } from "../components/ShoppingProgress";
import {
  PaymentMethodSelection,
  ShippingMethodSelection,
} from "../components/checkout/MethodSelection";
import { CarrierMapPicker, type ChosenPoint } from "../components/checkout/CarrierMapPicker";
import { PickupPointSelect } from "../components/checkout/PickupPointSelect";
import { Message } from "../components/Message";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";
import type { Customer } from "../account";
import { attemptFor, changedSince, inFlightWrite, uncertainWrite, type WriteAttempt } from "../idempotency";
import { formatPrice, useStore } from "../storeContext";
import { useRequest, type RequestState } from "../useRequest";
import { deliveryPriceChanged, readChangedShippingQuote, shippingMethodErrors, type ShippingQuote } from "../shippingQuotes";
import { DeliveryQuoteReview, UnavailableDeliveryMethods } from "../components/checkout/DeliveryQuoteReview";

export function CheckoutPage() {
  const { t } = useTranslation(["checkout", "cart"]);
  const cartState = useCart();
  const customerState = useCustomer();
  const store = useStore();

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

  return (
    <CheckoutForm
      key={store.id}
      cart={cartState.cart}
      cartPending={cartState.pending}
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
  cartPending,
  customer,
  adjusted,
  applyCart,
  reloadCart,
  acknowledgeAdjustment,
}: {
  cart: Cart;
  cartPending: boolean;
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
  const [phone, setPhone] = useState(customer?.phone ?? "");
  const [billing, setBilling] = useState<AddressDraft>(() => ({
    ...emptyAddress(),
    fullName: customer
      ? (customer.firstName + " " + customer.lastName).trim()
      : "",
  }));
  const [shipElsewhere, setShipElsewhere] = useState(false);
  const [shippingAddress, setShippingAddress] =
    useState<AddressDraft>(emptyAddress);
  const [shippingCode, setShippingCode] = useState<string | null>(null);
  const [paymentCode, setPaymentCode] = useState<string | null>(null);
  const [pickupPointCode, setPickupPointCode] = useState("");
  const [mapPoint, setMapPoint] = useState<ChosenPoint | null>(null);
  const [showValidation, setShowValidation] = useState(false);
  const [failed, setFailed] = useState<unknown | null>(null);
  const [refreshFailure, setRefreshFailure] = useState<unknown | null>(null);
  const [reviewKind, setReviewKind] = useState<"cart" | "shipping" | "inFlight" | "mismatch" | null>(null);
  const [priceChange, setPriceChange] = useState<{ scope: string; quote: ShippingQuote; previousPrice: number } | null>(null);
  const [shippingRefusal, setShippingRefusal] = useState<{ scope: string; messages: string[] } | null>(null);
  const [refreshingCart, setRefreshingCart] = useState(false);
  const [pending, setPending] = useState(false);
  const submitLock = useRef(false);
  const [attempt, setAttempt] = useState<WriteAttempt<CheckoutRequest> | null>(null);
  const validationSummary = useRef<HTMLDivElement>(null);
  const reviewNotice = useRef<HTMLDivElement>(null);
  const submitButton = useRef<HTMLButtonElement>(null);
  const alive = useRef(true);
  const recoveryRead = useRef<AbortController | null>(null);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; recoveryRead.current?.abort(); };
  }, []);

  const countryInput = (shipElsewhere ? shippingAddress : billing).country.trim();
  const invalidCountry = countryInput !== "" && !isCountryCode(countryInput);
  const country = countryInput.toUpperCase() || null;
  // Weight and destination both affect the server's offer. A changed scope never borrows old availability.
  const methodsScope = JSON.stringify([store.id, cart, country, invalidCountry]);
  const methodsRequest = useRequest(methodsScope, async (signal) => {
    if (invalidCountry) return null;
    const answer = await getCheckoutMethods(country, signal);
    if (answer.destinationCountry !== country) throw new HttpError("invalid-response", null);
    return answer;
  });
  const methods = methodsRequest.status === "ready" ? methodsRequest.data : null;
  const methodsRefreshing = methodsRequest.status === "loading" ||
    (methodsRequest.status === "ready" && methodsRequest.refreshing);
  const methodsError = methodsRequest.status === "error" || methodsRequest.status === "not-found"
    ? methodsRequest.error : methodsRequest.status === "ready" ? methodsRequest.refreshError : null;
  const methodsReady = methods !== null && !invalidCountry && !methodsRefreshing && methodsError === null;

  const chosenShipping = methods?.shippingMethods.find(method => method.code === shippingCode);
  const chosenPayment = methods?.paymentMethods.find(method => method.code === paymentCode);
  const chosenShippingCode = chosenShipping?.code ?? "";
  const shippingScope = JSON.stringify([methodsScope, shippingCode]);
  const currentPriceChange = priceChange?.scope === shippingScope ? priceChange : null;
  const currentShippingRefusal = shippingRefusal?.scope === methodsScope ? shippingRefusal.messages : [];
  const chosenPaymentCode = chosenPayment?.code ?? "";
  // Defaults are only for the first offer. A removed choice stays empty until the shopper chooses again.
  if (methodsReady && shippingCode === null) setShippingCode(methods.shippingMethods[0]?.code ?? "");
  else if (methodsReady && shippingCode && !chosenShipping) setShippingCode("");
  if (methodsReady && paymentCode === null) setPaymentCode(methods.paymentMethods[0]?.code ?? "");
  else if (methodsReady && paymentCode && !chosenPayment) setPaymentCode("");

  const pickupScope = JSON.stringify([methodsScope, chosenShippingCode, chosenShipping?.requiresPickupPoint, chosenShipping?.pickupPointChoice, chosenShipping?.mapParcel]);
  const [pointScope, setPointScope] = useState(pickupScope);
  const [pointReset, setPointReset] = useState(false);
  if (pointScope !== pickupScope) {
    setPointScope(pickupScope);
    if (pickupPointCode || mapPoint) setPointReset(true);
    setPickupPointCode("");
    setMapPoint(null);
  }
  const fromOurList = methodsReady && chosenShipping?.pickupPointChoice === "list";
  const inTheCarriersMap = methodsReady && chosenShipping?.pickupPointChoice === "carrier-map";
  const pickupPoints = useRequest(
    "checkout-pickup:" + pickupScope + (fromOurList ? ":list" : ":none"),
    (signal) => fromOurList ? getPickupPoints(chosenShippingCode, signal) : Promise.resolve([]),
  );
  if (fromOurList && pickupPoints.status === "ready" && !pickupPoints.refreshing &&
    pickupPoints.refreshError === null && pickupPointCode &&
    !pickupPoints.data.some(point => point.code === pickupPointCode)) {
    setPickupPointCode("");
    setPointReset(true);
  }
  const chosenPointCode = inTheCarriersMap ? (mapPoint?.id ?? "") : pickupPointCode;
  const listedPoint = fromOurList && pickupPoints.status === "ready"
    ? pickupPoints.data.find((point) => point.code === pickupPointCode)
    : undefined;
  const pickupLabel = inTheCarriersMap ? mapPoint?.label ?? null : listedPoint
    ? `${listedPoint.name} — ${listedPoint.line1}, ${listedPoint.postalCode} ${listedPoint.city}, ${listedPoint.country}`
    : null;
  // The carrier issued this key to be used in this page; without it its map cannot be opened at all.
  const carrierKey =
    store.providerKeys.find((published) => published.provider === "packeta")?.key ??
    null;

  const issues = checkoutIssues({
    email: customer?.email ?? email,
    phone,
    billing,
    shipElsewhere,
    shippingAddress,
    shippingMethodCode: chosenShippingCode,
    paymentMethodCode: chosenPaymentCode,
    pickupRequired: chosenShipping?.requiresPickupPoint ?? false,
    // A point chosen in the carrier's map is not in any list of ours, so there is nothing to check it
    // against here; the server asks the carrier, which is the only one that can answer.
    pickupPointCode: chosenPointCode,
    pickupPoints: inTheCarriersMap ? null : pickupPoints,
    messages: {
      email: t("checkout:emailIssue"),
      phone: t("checkout:phoneIssue"),
      billing: t("checkout:billingIssue"),
      shipping: t("checkout:shippingIssue"),
      shippingMethod: t("checkout:shippingMethodIssue"),
      paymentMethod: t("checkout:paymentMethodIssue"),
      pickup: t("checkout:pickupIssue"),
    },
  });
  const requestBody: CheckoutRequest = {
    email: (customer?.email ?? email).trim(),
    phone: phone.trim(),
    billingAddress: toAddress(billing),
    shippingAddress: shipElsewhere ? toAddress(shippingAddress) : null,
    paymentMethodCode: chosenPaymentCode,
    shippingMethodCode: chosenShippingCode,
    pickupPointCode: chosenShipping?.requiresPickupPoint ? chosenPointCode : null,
    expectedShippingPrice: chosenShipping?.price ?? 0,
  };
  const changedAfterUncertain = failed !== null && (uncertainWrite(failed) || inFlightWrite(failed)) &&
    changedSince(attempt, requestBody);
  const mustReview = reviewKind !== null || adjusted || changedAfterUncertain;
  useEffect(() => {
    if (mustReview && !pending) reviewNotice.current?.focus();
  }, [mustReview, pending]);
  const pickupUnavailable = fromOurList && pickupPoints.status === "ready" &&
    (pickupPoints.refreshing || pickupPoints.refreshError !== null);
  const canSubmit =
    issues.length === 0 &&
    !mustReview &&
    !pending &&
    !cartPending &&
    !pickupUnavailable &&
    !refreshingCart &&
    methodsReady &&
    country !== null &&
    refreshFailure === null;

  function chooseShipping(code: string) {
    setShippingCode(code);
    setPickupPointCode("");
    setMapPoint(null);
    setPointReset(false);
    setShowValidation(false);
    setShippingRefusal(null);
  }

  async function refreshAfterConflict() {
    recoveryRead.current?.abort();
    const controller = new AbortController();
    recoveryRead.current = controller;
    setRefreshingCart(true);
    setRefreshFailure(null);
    methodsRequest.reload();
    if (fromOurList) pickupPoints.reload();
    try {
      const current = await getCart(controller.signal);
      if (alive.current && !controller.signal.aborted) applyCart(current);
    } catch (error) {
      if (alive.current && !controller.signal.aborted) setRefreshFailure(error);
    } finally {
      if (alive.current && !controller.signal.aborted) setRefreshingCart(false);
    }
  }

  function reviewChanges() {
    if (refreshingCart || !methodsReady || refreshFailure !== null) return;
    if (reviewKind !== "inFlight" || changedAfterUncertain) setAttempt(null);
    acknowledgeAdjustment();
    setReviewKind(null);
    setFailed(null);
    setRefreshFailure(null);
    setPriceChange(null);
    setShippingRefusal(null);
    window.requestAnimationFrame(() => { if (alive.current) submitButton.current?.focus(); });
  }

  function focusTarget(target: string) {
    const field = document.querySelector<HTMLElement>(
      '[name="' + CSS.escape(target) + '"]',
    );
    field?.focus();
    field?.scrollIntoView?.({ block: "center" });
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

    if (!canSubmit) return;

    submitLock.current = true;
    setFailed(null);
    setRefreshFailure(null);
    setPending(true);
    try {
      const current = attemptFor(attempt, requestBody);
      setAttempt(current);
      const order = await placeOrder(current.payload, current.key);
      if (!alive.current) return;
      setAttempt(null);
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
      if (!alive.current) return;
      setFailed(error);
      if (statusOf(error) === 409) {
        if (inFlightWrite(error)) {
          setReviewKind("inFlight");
        } else {
          let changedQuote: ShippingQuote | null = null;
          if (chosenShipping) {
            try { changedQuote = readChangedShippingQuote(error, store.currency, cart, chosenShipping); }
            catch (invalid) { setFailed(invalid); }
          }
          setPriceChange(changedQuote ? { scope: shippingScope, quote: changedQuote, previousPrice: chosenShipping!.price } : null);
          setReviewKind(changedQuote ? "shipping" : "cart");
          await refreshAfterConflict();
        }
      } else if (statusOf(error) === 400 && shippingMethodErrors(error).length > 0) {
        setShippingRefusal({ scope: methodsScope, messages: shippingMethodErrors(error) });
        setReviewKind("cart");
        await refreshAfterConflict();
      } else if (statusOf(error) === 422) {
        setReviewKind("mismatch");
      }
    } finally {
      if (alive.current) setPending(false);
      submitLock.current = false;
    }
  }

  return (
    <section className="shopping-checkout-page" aria-labelledby="checkout-heading">
      <ShoppingProgress current="checkout" />
      <header className="shopping-page-heading">
        <h1 id="checkout-heading">{t("checkout:title")}</h1>
        <Link to="/cart">{t("checkout:backToCart")}</Link>
      </header>
      <form
        className="checkout"
        noValidate
        aria-busy={pending || undefined}
        onSubmit={(event) => void submit(event)}
      >
        <fieldset className="checkout-fields" disabled={pending || cartPending}>
          <legend className="sr-only">{t("checkout:orderDetails")}</legend>
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
              <InlineMessage title={t(changedAfterUncertain ? "checkout:uncertainChangedTitle" : currentPriceChange ? "checkout:shippingPriceChangedTitle" : reviewKind === "inFlight" ? "checkout:inFlightTitle" : reviewKind === "mismatch" ? "checkout:keyMismatchTitle" : "checkout:reviewTitle")}>
                {!currentPriceChange && <p>{t(changedAfterUncertain ? "checkout:uncertainChangedBody" : reviewKind === "inFlight" ? "checkout:inFlightBody" : reviewKind === "mismatch" ? "checkout:keyMismatchBody" : "checkout:reviewBody")}</p>}
                {currentPriceChange && <DeliveryQuoteReview quote={currentPriceChange.quote} previousPrice={currentPriceChange.previousPrice} />}
                <button
                  type="button"
                  disabled={refreshingCart || !methodsReady || refreshFailure !== null}
                  onClick={reviewChanges}
                >
                  {refreshingCart || methodsRefreshing
                    ? t("checkout:refreshingOrder")
                    : currentPriceChange && chosenShipping
                      ? t("checkout:reviewShippingPriceAction", { price: formatPrice(chosenShipping.price, store) })
                      : t(changedAfterUncertain || reviewKind === "mismatch" ? "checkout:newAttemptAction" : reviewKind === "inFlight" ? "checkout:retryReviewAction" : "checkout:reviewAction")}
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
              {!(deliveryPriceChanged(failed) && currentPriceChange) && <RequestError error={failed} operation="checkout"
                fieldTargets={{ shippingMethodCode: "checkout-shipping-heading", expectedShippingPrice: "checkout-shipping-heading" }} />}
              {!mustReview && (
                <p className="hint">{t(uncertainWrite(failed) ? "checkout:sameRequestRetry" : "checkout:noAutomaticRetry")}</p>
              )}
            </>
          )}

          <section
            className="checkout-section checkout-contact"
            aria-labelledby="checkout-contact-heading"
          >
            <h2 id="checkout-contact-heading" tabIndex={-1}>{t("checkout:contact")}</h2>
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
            <label>
              <span>{t("checkout:phone")}</span>
              <input
                name="phone"
                type="tel"
                autoComplete="tel"
                value={phone}
                required
                aria-invalid={(showValidation && !isPhone(phone)) || undefined}
                aria-describedby="checkout-phone-hint"
                onChange={(event) => setPhone(event.target.value)}
              />
            </label>
            <p className="hint" id="checkout-phone-hint">
              {t("checkout:phoneHint")}
            </p>
            {customer === null && (
              <p className="hint">
                <Trans
                  ns="checkout"
                  i18nKey="guestPrompt"
                  components={{ signIn: <Link to="/account/sign-in?returnTo=%2Fcheckout" /> }}
                />
              </p>
            )}
          </section>

          <fieldset className="checkout-section">
            <legend id="checkout-billing-heading" tabIndex={-1}>{t("checkout:billingAddress")}</legend>
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
              <legend id="checkout-shipping-address-heading" tabIndex={-1}>{t("checkout:shippingAddress")}</legend>
              <AddressFields
                prefix="shipping"
                value={shippingAddress}
                onChange={setShippingAddress}
                showErrors={showValidation}
              />
            </fieldset>
          )}

          <div className="checkout-method-status" aria-live="polite" aria-atomic="true">
            {invalidCountry ? <p className="hint">{t("checkout:destinationCountryNeeded")}</p>
              : methodsRefreshing ? <p role="status">{t("checkout:loadingMethods")}</p> : null}
            {methodsError !== null && <RequestError error={methodsError} operation="read"
              onRetry={methodsRequest.reload}
              fieldTargets={{ country: shipElsewhere ? "shipping.country" : "billing.country" }} />}
            {methodsReady && (methods.shippingMethods.length === 0 || methods.paymentMethods.length === 0) &&
              <InlineMessage title={t("checkout:unavailableTitle")}>
                <p>{t(methods.shippingMethods.length === 0 && methods.paymentMethods.length === 0
                  ? "checkout:unavailableStore" : methods.shippingMethods.length === 0
                    ? "checkout:noShippingMethods" : "checkout:noPaymentMethods")}</p>
                <button type="button" onClick={methodsRequest.reload}>{t("checkout:refreshMethods")}</button>
              </InlineMessage>}
            {methodsReady && shippingCode === "" && methods.shippingMethods.length > 0 &&
              <p>{t("checkout:shippingSelectionChanged")}</p>}
            {methodsReady && paymentCode === "" && methods.paymentMethods.length > 0 &&
              <p>{t("checkout:paymentSelectionChanged")}</p>}
            {pointReset && <p>{t("checkout:pickupSelectionChanged")}</p>}
          </div>

          <ShippingMethodSelection
            methods={methods?.shippingMethods ?? []}
            selectedCode={chosenShippingCode}
            store={store}
            disabled={pending || !methodsReady}
            onChange={chooseShipping}
            error={currentShippingRefusal}
          />
          {methodsReady && <UnavailableDeliveryMethods methods={methods.unavailableShippingMethods ?? []} cart={cart} />}

          {fromOurList && (
            <PickupPointSelect
              points={pickupPoints}
              value={pickupPointCode}
              disabled={pending}
              showError={showValidation}
              onChange={(code) => { setPickupPointCode(code); setPointReset(false); }}
            />
          )}

          {inTheCarriersMap && (
            <CarrierMapPicker
              key={pickupScope}
              apiKey={carrierKey}
              language={store.culture.split("-")[0]}
              chosen={mapPoint}
              disabled={pending}
              showError={showValidation}
              parcel={chosenShipping?.mapParcel}
              onChoose={(point) => { setMapPoint(point); setPointReset(false); }}
            />
          )}

          <PaymentMethodSelection
            methods={methods?.paymentMethods ?? []}
            selectedCode={chosenPaymentCode}
            disabled={pending || !methodsReady}
            onChange={setPaymentCode}
          />
          <CheckoutReview request={requestBody} shippingName={chosenShipping?.name ?? ""} paymentName={chosenPayment?.name ?? ""}
            complete={issues.length === 0 && methodsReady}
            pickupLabel={pickupLabel} />
        </fieldset>

        <CheckoutSummary
          cart={cart}
          shipping={methodsReady ? chosenShipping : undefined}
          store={store}
          pending={pending}
        >
          <div className="checkout-actions">
            <button
              ref={submitButton}
              type="submit"
              className="button checkout-submit"
              disabled={pending || cartPending || refreshingCart || methodsRefreshing || (fromOurList && pickupPoints.status === "ready" && pickupPoints.refreshing)}
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
                {t(cartPending ? "cart:updatingCart" : "checkout:completeOrder")}
              </p>
            )}
          </div>
        </CheckoutSummary>
      </form>
    </section>
  );
}

function checkoutIssues({
  email,
  phone,
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
  phone: string;
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
  > | null;
  messages: {
    email: string;
    phone: string;
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
  if (!isPhone(phone))
    issues.push({ target: "phone", message: messages.phone });
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
    // A point chosen in the carrier's map cannot be looked up in a list we do not have, so here it only has
    // to have been chosen; whether it is a real one is the carrier's answer, asked for on the server.
    const chosen =
      pickupPoints === null
        ? pickupPointCode.length > 0
        : pickupPoints.status === "ready" &&
          pickupPoints.data.some((point) => point.code === pickupPointCode);
    if (!chosen)
      issues.push({
        target: "pickupPointCode",
        message: messages.pickup,
      });
  }
  return issues;
}

// The same shape the server accepts: enough to tell a telephone number from a line of prose, and no more.
function isPhone(value: string) {
  const digits = value.replace(/\D/g, "").length;
  return (
    value.trim().length <= 30 &&
    digits >= 6 &&
    /^[\d\s+\-()/]*$/.test(value.trim())
  );
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
