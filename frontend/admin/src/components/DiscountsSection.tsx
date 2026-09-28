import { useTranslation } from "react-i18next";
import { api, type Discount } from "../api";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { RequestError } from "./ui/RequestError";
export function DiscountsSection({
  storeId,
  money,
}: {
  storeId: string;
  money: Intl.NumberFormat;
}) {
  const { t } = useTranslation(["discounts", "common", "errors"]);
  const [discounts, reload] = useRequest(`discounts:${storeId}`, (signal) =>
    api.discounts(storeId, signal),
  );
  const [error, run] = useAction(reload);
  const codes: Discount[] = discounts.status === "ready" ? discounts.data : [];
  const kindLabel = (discount: Discount) =>
    discount.kind === "Percentage"
      ? t("discounts:percentageLabel", { value: discount.value })
      : discount.kind === "Amount"
        ? t("discounts:amountLabel", { value: discount.value })
        : t("discounts:kinds.FreeShipping");
  return (
    <section>
      <h2>{t("discounts:title")}</h2>
      <p className="hint">{t("discounts:hint")}</p>
      {discounts.status === "error" && (
        <RequestError
          error={discounts.error}
          operation="read"
          onRetry={reload}
        />
      )}
      {discounts.status === "ready" && discounts.refreshError !== null && (
        <RequestError
          error={discounts.refreshError}
          operation="read"
          onRetry={reload}
        />
      )}
      {error !== null && <RequestError error={error} operation="write" />}
      {codes.map((discount) => (
        <form
          key={discount.code}
          className="inline-form"
          action={(form) =>
            run(async () => {
              await api.updateDiscount(storeId, discount.code, {
                code: discount.code,
                name: String(form.get("name")),
                kind: discount.kind,
                value: Number(form.get("value")),
                minimumOrderAmount: optionalNumber(
                  form.get("minimumOrderAmount"),
                ),
                startsAt: null,
                endsAt: optionalDate(form.get("endsAt")),
                maxRedemptions: optionalNumber(form.get("maxRedemptions")),
                maxRedemptionsPerCustomer: optionalNumber(
                  form.get("maxRedemptionsPerCustomer"),
                ),
                isActive: form.get("isActive") === "on",
              });
              reload();
            })
          }
        >
          <strong className="chip">{discount.code}</strong>
          <input name="name" defaultValue={discount.name} required />
          <span className="chip">{kindLabel(discount)}</span>
          {discount.kind !== "FreeShipping" && (
            <label>
              {t("discounts:value")}{" "}
              <input
                name="value"
                type="number"
                min="0"
                step="0.01"
                defaultValue={discount.value}
                required
              />
            </label>
          )}
          <label>
            {t("discounts:minimum")}{" "}
            <input
              name="minimumOrderAmount"
              type="number"
              min="0"
              step="0.01"
              defaultValue={discount.minimumOrderAmount ?? ""}
            />
          </label>
          <label>
            {t("discounts:ends")}{" "}
            <input
              name="endsAt"
              type="date"
              defaultValue={discount.endsAt?.slice(0, 10) ?? ""}
            />
          </label>
          <label>
            {t("discounts:uses")}{" "}
            <input
              name="maxRedemptions"
              type="number"
              min="1"
              defaultValue={discount.maxRedemptions ?? ""}
            />
          </label>
          <label>
            {t("discounts:perCustomer")}{" "}
            <input
              name="maxRedemptionsPerCustomer"
              type="number"
              min="1"
              defaultValue={discount.maxRedemptionsPerCustomer ?? ""}
            />
          </label>
          <span className="hint">
            {discount.maxRedemptions === null
              ? t("discounts:used", { count: discount.redemptions })
              : t("discounts:usedOf", {
                  count: discount.redemptions,
                  maximum: discount.maxRedemptions,
                })}
          </span>
          <label>
            <input
              name="isActive"
              type="checkbox"
              defaultChecked={discount.isActive}
            />{" "}
            {t("discounts:usable")}
          </label>
          <button type="submit">{t("common:save")}</button>
        </form>
      ))}
      <form
        className="inline-form"
        action={(form) =>
          run(async () => {
            await api.createDiscount(storeId, {
              code: String(form.get("code")),
              name: String(form.get("name")),
              kind: String(form.get("kind")),
              value: Number(form.get("value")),
              minimumOrderAmount: optionalNumber(
                form.get("minimumOrderAmount"),
              ),
              startsAt: null,
              endsAt: optionalDate(form.get("endsAt")),
              maxRedemptions: optionalNumber(form.get("maxRedemptions")),
              maxRedemptionsPerCustomer: optionalNumber(
                form.get("maxRedemptionsPerCustomer"),
              ),
              isActive: true,
            });
            reload();
          })
        }
      >
        <input name="code" placeholder="NEWCODE" maxLength={40} required />
        <input
          name="name"
          placeholder={t("discounts:namePlaceholder")}
          required
        />
        <select name="kind" defaultValue="Percentage">
          <option value="Percentage">{t("discounts:kinds.Percentage")}</option>
          <option value="Amount">{t("discounts:kinds.Amount")}</option>
          <option value="FreeShipping">
            {t("discounts:kinds.FreeShipping")}
          </option>
        </select>
        <input
          name="value"
          type="number"
          min="0"
          step="0.01"
          placeholder={t("discounts:valuePlaceholder")}
          defaultValue="10"
        />
        <input
          name="minimumOrderAmount"
          type="number"
          min="0"
          step="0.01"
          placeholder={t("discounts:minimumOrder")}
        />
        <input name="endsAt" type="date" />
        <input
          name="maxRedemptions"
          type="number"
          min="1"
          placeholder={t("discounts:totalUses")}
        />
        <input
          name="maxRedemptionsPerCustomer"
          type="number"
          min="1"
          placeholder={t("discounts:perCustomer")}
        />
        <button type="submit">{t("discounts:addCode")}</button>
      </form>
      <p className="hint">
        {t("discounts:example", { amount: money.format(50) })}
      </p>
    </section>
  );
}
function optionalNumber(value: FormDataEntryValue | null) {
  const text = String(value ?? "").trim();
  return text ? Number(text) : null;
}
function optionalDate(value: FormDataEntryValue | null) {
  const text = String(value ?? "").trim();
  return text ? new Date(`${text}T23:59:59Z`).toISOString() : null;
}
