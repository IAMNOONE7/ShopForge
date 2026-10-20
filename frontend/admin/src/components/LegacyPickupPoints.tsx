import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { api } from "../api";
import { useRequest } from "../useRequest";
import { useAction } from "../useAction";
import { RequestError } from "./ui/RequestError";

export function LegacyPickupPoints({ storeId, allowed, storeStale }: { storeId: string; allowed: boolean; storeStale: boolean }) {
  const { t } = useTranslation(["pickupPoints", "common", "methodSetup"]);
  const [state, reloadPickupPoints] = useRequest(`legacy-points:${storeId}`, signal => api.pickupPoints(storeId, signal));
  const [error, run, pending] = useAction(() => undefined);
  const active = useRef(true);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const ready = state.status === "ready" && !state.refreshing && state.refreshError === null;
  const points = state.status === "ready" ? state.data : [];
  async function save(change: () => Promise<unknown>) {
    if (!allowed || storeStale || !ready || pending) return;
    await run(async () => { try { await change(); } finally { if (active.current) reloadPickupPoints(); } });
  }
  const readError = state.status === "error" || state.status === "not-found" ? state.error : state.status === "ready" ? state.refreshError : null;
  return <details className="legacy-methods"><summary>{t("methodSetup:legacyPoints")}</summary><p>{t("methodSetup:legacyHint")}</p>
    {readError !== null && <RequestError error={readError} operation="read" onRetry={reloadPickupPoints} />}
    {error !== null && <RequestError error={error} operation="write" />}
    <fieldset disabled={!allowed || storeStale || !ready || pending}><legend className="sr-only">{t("methodSetup:legacyPoints")}</legend>
      <h3>{t("pickupPoints:title")}</h3>
      <p className="hint">{t("pickupPoints:hint")}</p>
      {points.map((point) => (
        <form
          key={`${point.code}:${JSON.stringify(point)}`}
          className="inline-form"
          action={(form) =>
            save(async () => {
              await api.updatePickupPoint(storeId, point.code, {
                name: String(form.get("name")),
                line1: String(form.get("line1")),
                city: String(form.get("city")),
                postalCode: String(form.get("postalCode")),
                country: String(form.get("country")).toUpperCase(),
                isActive: form.get("isActive") === "on",
              });
            })
          }
        >
          <input name="name" aria-label={t("pickupPoints:newPoint")} defaultValue={point.name} required />
          <input name="line1" aria-label={t("pickupPoints:street")} defaultValue={point.line1} required />
          <input name="city" aria-label={t("pickupPoints:city")} defaultValue={point.city} required />
          <input name="postalCode" aria-label={t("pickupPoints:postalCode")} defaultValue={point.postalCode} required />
          <input
            name="country"
            aria-label={t("methodSetup:pointCountry")}
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
          save(async () => {
            await api.createPickupPoint(storeId, {
              name: String(form.get("name")),
              line1: String(form.get("line1")),
              city: String(form.get("city")),
              postalCode: String(form.get("postalCode")),
              country: String(form.get("country")).toUpperCase(),
              isActive: true,
            });
          })
        }
      >
        <input name="name" aria-label={t("pickupPoints:newPoint")} placeholder={t("pickupPoints:newPoint")} required />
        <input name="line1" aria-label={t("pickupPoints:street")} placeholder={t("pickupPoints:street")} required />
        <input name="city" aria-label={t("pickupPoints:city")} placeholder={t("pickupPoints:city")} required />
        <input
          name="postalCode"
          aria-label={t("pickupPoints:postalCode")}
          placeholder={t("pickupPoints:postalCode")}
          required
        />
        <input
          name="country"
          aria-label={t("methodSetup:pointCountry")}
          placeholder={t("pickupPoints:countryExample")}
          maxLength={2}
          required
        />
        <button type="submit">{t("common:add")}</button>
      </form>
    </fieldset></details>;
}
