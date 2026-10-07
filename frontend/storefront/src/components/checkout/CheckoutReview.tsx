import { useTranslation } from "react-i18next";
import type { CheckoutRequest } from "../../cart";
import { useStore } from "../../storeContext";

export function CheckoutReview({ request, shippingName, paymentName, pickupLabel, complete }: {
  request: CheckoutRequest;
  shippingName: string;
  paymentName: string;
  pickupLabel: string | null;
  complete: boolean;
}) {
  const { t } = useTranslation("checkout");
  const store = useStore();
  const address = request.shippingAddress ?? request.billingAddress;
  return (
    <section className="checkout-final-review" aria-labelledby="checkout-final-review-heading">
      <h2 id="checkout-final-review-heading" tabIndex={-1}>{t("checkDetails")}</h2>
      {!complete ? <p className="hint">{t("reviewDetailsHint")}</p> : (
        <dl>
          <div>
            <dt>{t("contact")} <a href="#checkout-contact-heading">{t("editContact")}</a></dt>
            <dd><bdi>{request.email}</bdi><br /><bdi>{request.phone}</bdi></dd>
          </div>
          <div>
            <dt>{t("shipping")} <a href="#checkout-shipping-heading">{t("editDelivery")}</a></dt>
            <dd>
              <bdi lang={store.culture}>{shippingName}</bdi>
              {request.pickupPointCode ? <p>{pickupLabel ?? request.pickupPointCode}</p> : (
                <p><bdi>{address.fullName}</bdi><br />{[address.line1, address.line2].filter(Boolean).join(", ")}<br />
                  {address.postalCode} {address.city}, {address.country}
                </p>
              )}
              {!request.pickupPointCode && <a href={request.shippingAddress ? "#checkout-shipping-address-heading" : "#checkout-billing-heading"}>{t("editAddress")}</a>}
            </dd>
          </div>
          <div>
            <dt>{t("payment")} <a href="#checkout-payment-heading">{t("editPayment")}</a></dt>
            <dd><bdi lang={store.culture}>{paymentName}</bdi></dd>
          </div>
        </dl>
      )}
    </section>
  );
}
