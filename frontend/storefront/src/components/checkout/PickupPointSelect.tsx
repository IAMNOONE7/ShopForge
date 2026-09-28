import { useTranslation } from "react-i18next";
import type { PickupPoint } from "../../cart";
import type { RequestState } from "../../useRequest";
import { InlineMessage } from "../ui/InlineMessage";
import { LoadingState } from "../ui/LoadingState";
import { RequestError } from "../ui/RequestError";

export function PickupPointSelect({
  points,
  value,
  disabled,
  showError,
  onChange,
}: {
  points: RequestState<PickupPoint[]>;
  value: string;
  disabled: boolean;
  showError: boolean;
  onChange: (code: string) => void;
}) {
  const { t } = useTranslation("checkout");

  if (points.status === "loading") {
    return (
      <div className="checkout-pickup-state">
        <h3>{t("pickupPoint")}</h3>
        <LoadingState label={t("loadingPickupPoints")} lines={2} />
      </div>
    );
  }

  if (points.status === "error" || points.status === "not-found") {
    return (
      <div className="checkout-pickup-state">
        <h3>{t("pickupPoint")}</h3>
        <RequestError
          error={points.error}
          operation="read"
          onRetry={points.reload}
        />
      </div>
    );
  }

  if (points.data.length === 0) {
    return (
      <div className="checkout-pickup-state">
        <h3>{t("pickupPoint")}</h3>
        <InlineMessage tone="error">{t("noPickupPoints")}</InlineMessage>
      </div>
    );
  }

  return (
    <div className="checkout-pickup-state">
      {points.refreshError !== null && (
        <RequestError
          error={points.refreshError}
          operation="read"
          onRetry={points.reload}
        />
      )}
      <label className="checkout-pickup-select">
        <span>{t("pickupPoint")}</span>
        <select
          name="pickupPointCode"
          value={value}
          disabled={disabled}
          required
          aria-invalid={(showError && value.length === 0) || undefined}
          onChange={(event) => onChange(event.target.value)}
        >
          <option value="">{t("choosePickup")}</option>
          {points.data.map((point) => (
            <option key={point.code} value={point.code}>
              {point.name} — {point.line1}, {point.city}, {point.postalCode},{" "}
              {point.country}
            </option>
          ))}
        </select>
      </label>
    </div>
  );
}
