import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type Stock } from "../api";
import { statusOf } from "../api/errors";
import { canManageCatalog, useSession } from "../session";
import { useAction } from "../useAction";
import { formatNumber } from "../utils/format";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

export function StockAdjustment({
  variantId,
  current,
  onChanged,
  onConflict,
}: {
  variantId: string;
  current: Stock;
  onChanged: () => void;
  onConflict: () => void;
}) {
  const { t, i18n } = useTranslation("stock");
  const { user } = useSession();
  const [quantity, setQuantity] = useState(String(current.onHand));
  const [invalid, setInvalid] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, run, pending] = useAction(onChanged);
  const parsed = /^[0-9]+$/.test(quantity.trim()) && Number(quantity) <= 2147483647
    ? Number(quantity.trim())
    : null;
  const belowReserved = parsed !== null && parsed < current.reserved;

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    if (parsed === null) {
      setInvalid(true);
      document.getElementById("stock-quantity")?.focus();
      return;
    }
    await run(async () => {
      try {
        const result = await api.setStock(variantId, parsed);
        setQuantity(String(result.onHand));
        setSaved(true);
      } catch (failure) {
        if (statusOf(failure) === 409) onConflict();
        throw failure;
      }
    });
  }

  return (
    <section className="physical-panel stock-adjustment" aria-labelledby="stock-adjustment-title">
      <h2 id="stock-adjustment-title">{t("adjustTitle")}</h2>
      <p className="hint">{t("adjustHint")}</p>
      {!canManageCatalog(user.role) ? (
        <InlineMessage>{t("readOnly")}</InlineMessage>
      ) : (
        <form className="stock-adjustment-form" noValidate onSubmit={save}>
          <Field
            id="stock-quantity"
            name="quantity"
            label={t("newOnHand")}
            hint={t("absoluteHint")}
            error={invalid ? t("quantityInvalid") : undefined}
            value={quantity}
            inputMode="numeric"
            onChange={(event) => {
              setQuantity(event.target.value);
              setInvalid(false);
              setSaved(false);
            }}
          />
          {belowReserved && (
            <InlineMessage tone="info">
              {t("belowReserved", {
                reserved: formatNumber(current.reserved, i18n.resolvedLanguage),
              })}
            </InlineMessage>
          )}
          <Button type="submit" busy={pending} busyLabel={t("saving")}>
            {t("save")}
          </Button>
          {saved && <InlineMessage tone="success">{t("saved")}</InlineMessage>}
          {error !== null && (statusOf(error) === 409
            ? <InlineMessage tone="error">{t("conflict")}</InlineMessage>
            : <RequestError error={error} operation="write" />)}
        </form>
      )}
    </section>
  );
}
