import { useTranslation } from "react-i18next";
import { api, type PaymentMethod, type ShippingMethod } from "../api";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
type Props = {
  storeId: string;
  money: Intl.NumberFormat;
  run: (change: () => Promise<unknown>) => Promise<void>;
};
export function MethodsSection({ storeId, money, run }: Props) {
  const { t } = useTranslation(["methods", "pickupPoints", "stores", "common"]);
  const [providers, reloadProviders] = useRequest(
    `payment-providers:${storeId}`,
    (signal) => api.paymentProviders(storeId, signal),
  );
  const [payment, reloadPayment] = useRequest(
    `payment-methods:${storeId}`,
    (signal) => api.paymentMethods(storeId, signal),
  );
  const [shippingProviders, reloadShippingProviders] = useRequest(
    `shipping-providers:${storeId}`,
    (signal) => api.shippingProviders(storeId, signal),
  );
  const [shipping, reloadShipping] = useRequest(
    `shipping-methods:${storeId}`,
    (signal) => api.shippingMethods(storeId, signal),
  );
  const [pickupPoints, reloadPickupPoints] = useRequest(
    `pickup-points:${storeId}`,
    (signal) => api.pickupPoints(storeId, signal),
  );
  const paymentMethods: PaymentMethod[] =
    payment.status === "ready" ? payment.data : [];
  const providerKeys = providers.status === "ready" ? providers.data : [];
  const shippingProviderKeys =
    shippingProviders.status === "ready" ? shippingProviders.data : [];
  const points = pickupPoints.status === "ready" ? pickupPoints.data : [];
  const shippingMethods: ShippingMethod[] =
    shipping.status === "ready" ? shipping.data : [];
  return (
    <section>
      <h2>{t("methods:title")}</h2>
      <p className="hint">{t("methods:hint")}</p>
      {providers.status === "error" && (
        <RequestError
          error={providers.error}
          operation="read"
          onRetry={reloadProviders}
        />
      )}
      {payment.status === "error" && (
        <RequestError
          error={payment.error}
          operation="read"
          onRetry={reloadPayment}
        />
      )}
      {shippingProviders.status === "error" && (
        <RequestError
          error={shippingProviders.error}
          operation="read"
          onRetry={reloadShippingProviders}
        />
      )}
      {shipping.status === "error" && (
        <RequestError
          error={shipping.error}
          operation="read"
          onRetry={reloadShipping}
        />
      )}
      {pickupPoints.status === "error" && (
        <RequestError
          error={pickupPoints.error}
          operation="read"
          onRetry={reloadPickupPoints}
        />
      )}
      <h3>{t("methods:payment")}</h3>
      <p className="hint">{t("methods:paymentHint")}</p>
      {paymentMethods.map((method) => (
        <form
          key={method.code}
          className="inline-form"
          action={(form) =>
            run(async () => {
              await api.updatePaymentMethod(storeId, method.code, {
                name: String(form.get("name")),
                isActive: form.get("isActive") === "on",
              });
              reloadPayment();
            })
          }
        >
          <input name="name" defaultValue={method.name} required />
          <span className="chip">{method.providerKey}</span>
          <label>
            <input
              name="isActive"
              type="checkbox"
              defaultChecked={method.isActive}
            />{" "}
            {t("methods:offered")}
          </label>
          <button type="submit">{t("common:save")}</button>
        </form>
      ))}
      <form
        className="inline-form"
        action={(form) =>
          run(async () => {
            await api.createPaymentMethod(
              storeId,
              String(form.get("name")),
              String(form.get("providerKey")),
            );
            reloadPayment();
          })
        }
      >
        <input name="name" placeholder={t("methods:newPayment")} required />
        <select name="providerKey" defaultValue="manual">
          {providerKeys.map((key) => (
            <option key={key} value={key}>
              {key}
            </option>
          ))}
        </select>
        <button type="submit">{t("common:add")}</button>
      </form>
      <h3>{t("methods:shipping")}</h3>
      {shippingMethods.map((method) => (
        <form
          key={method.code}
          className="inline-form"
          action={(form) =>
            run(async () => {
              await api.updateShippingMethod(storeId, method.code, {
                name: String(form.get("name")),
                price: Number(form.get("price")),
                vatRate: Number(form.get("vatRate")),
                isActive: form.get("isActive") === "on",
                requiresPickupPoint: form.get("requiresPickupPoint") === "on",
              });
              reloadShipping();
            })
          }
        >
          <input name="name" defaultValue={method.name} required />
          <label>
            {t("methods:price")}{" "}
            <input
              name="price"
              type="number"
              min="0"
              step="0.01"
              defaultValue={method.price}
              required
            />
          </label>
          <label>
            {t("methods:vatPercent")}{" "}
            <input
              name="vatRate"
              type="number"
              min="0"
              max="100"
              step="0.01"
              defaultValue={method.vatRate}
              required
            />
          </label>
          <span className="hint">{money.format(method.price)}</span>
          <span className="chip">{method.providerKey}</span>
          <label>
            <input
              name="requiresPickupPoint"
              type="checkbox"
              defaultChecked={method.requiresPickupPoint}
            />{" "}
            {t("methods:needsPickup")}
          </label>
          <label>
            <input
              name="isActive"
              type="checkbox"
              defaultChecked={method.isActive}
            />{" "}
            {t("methods:offered")}
          </label>
          <button type="submit">{t("common:save")}</button>
        </form>
      ))}
      <form
        className="inline-form"
        action={(form) =>
          run(async () => {
            await api.createShippingMethod(storeId, {
              name: String(form.get("name")),
              providerKey: String(form.get("providerKey")),
              price: Number(form.get("price")),
              vatRate: Number(form.get("vatRate")),
              requiresPickupPoint: form.get("requiresPickupPoint") === "on",
            });
            reloadShipping();
          })
        }
      >
        <input name="name" placeholder={t("methods:newShipping")} required />
        <input
          name="price"
          type="number"
          min="0"
          step="0.01"
          placeholder={t("methods:price")}
          required
        />
        <input
          name="vatRate"
          type="number"
          min="0"
          max="100"
          step="0.01"
          placeholder={t("methods:vatPercent")}
          defaultValue="21"
          required
        />
        <select name="providerKey" defaultValue="manual">
          {shippingProviderKeys.map((key) => (
            <option key={key} value={key}>
              {key}
            </option>
          ))}
        </select>
        <label>
          <input name="requiresPickupPoint" type="checkbox" />{" "}
          {t("methods:needsPickup")}
        </label>
        <button type="submit">{t("common:add")}</button>
      </form>
      <h3>{t("pickupPoints:title")}</h3>
      <p className="hint">{t("pickupPoints:hint")}</p>
      {points.map((point) => (
        <form
          key={point.code}
          className="inline-form"
          action={(form) =>
            run(async () => {
              await api.updatePickupPoint(storeId, point.code, {
                name: String(form.get("name")),
                line1: String(form.get("line1")),
                city: String(form.get("city")),
                postalCode: String(form.get("postalCode")),
                country: String(form.get("country")).toUpperCase(),
                isActive: form.get("isActive") === "on",
              });
              reloadPickupPoints();
            })
          }
        >
          <input name="name" defaultValue={point.name} required />
          <input name="line1" defaultValue={point.line1} required />
          <input name="city" defaultValue={point.city} required />
          <input name="postalCode" defaultValue={point.postalCode} required />
          <input
            name="country"
            defaultValue={point.country}
            maxLength={2}
            required
          />
          <label>
            <input
              name="isActive"
              type="checkbox"
              defaultChecked={point.isActive}
            />{" "}
            {t("pickupPoints:open")}
          </label>
          <button type="submit">{t("common:save")}</button>
        </form>
      ))}
      <form
        className="inline-form"
        action={(form) =>
          run(async () => {
            await api.createPickupPoint(storeId, {
              name: String(form.get("name")),
              line1: String(form.get("line1")),
              city: String(form.get("city")),
              postalCode: String(form.get("postalCode")),
              country: String(form.get("country")).toUpperCase(),
              isActive: true,
            });
            reloadPickupPoints();
          })
        }
      >
        <input name="name" placeholder={t("pickupPoints:newPoint")} required />
        <input name="line1" placeholder={t("pickupPoints:street")} required />
        <input name="city" placeholder={t("pickupPoints:city")} required />
        <input
          name="postalCode"
          placeholder={t("pickupPoints:postalCode")}
          required
        />
        <input
          name="country"
          placeholder={t("pickupPoints:countryExample")}
          maxLength={2}
          required
        />
        <button type="submit">{t("common:add")}</button>
      </form>
    </section>
  );
}
