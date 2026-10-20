import { useTranslation } from "react-i18next";
import { api, type PaymentMethod } from "../api";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
import { ShippingMethodsSection } from "./ShippingMethodsSection";
type Props = {
  storeId: string;
  canManage?: boolean;
  storeStale?: boolean;
  money: Intl.NumberFormat;
  run: (change: () => Promise<unknown>) => Promise<void>;
};
export function MethodsSection({ storeId, money, run, canManage = true, storeStale = false }: Props) {
  const { t } = useTranslation(["methods", "pickupPoints", "stores", "common"]);
  const [providers, reloadProviders] = useRequest(
    `payment-providers:${storeId}`,
    (signal) => api.paymentProviders(storeId, signal),
  );
  const [payment, reloadPayment] = useRequest(
    `payment-methods:${storeId}`,
    (signal) => api.paymentMethods(storeId, signal),
  );
  const [pickupPoints, reloadPickupPoints] = useRequest(
    `pickup-points:${storeId}`,
    (signal) => api.pickupPoints(storeId, signal),
  );
  const paymentMethods: PaymentMethod[] =
    payment.status === "ready" ? payment.data : [];
  const providerKeys = providers.status === "ready" ? providers.data : [];
  const points = pickupPoints.status === "ready" ? pickupPoints.data : [];
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
      <ShippingMethodsSection storeId={storeId} money={money} allowed={canManage} storeStale={storeStale} />
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
