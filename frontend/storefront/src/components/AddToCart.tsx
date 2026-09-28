import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { addToCart } from "../cart";
import { useCart } from "../cartContext";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

type Result =
  | { kind: "added"; count: number; capped: boolean }
  | { kind: "unchanged" }
  | { kind: "failed" }
  | null;

export function AddToCart({
  storeProductId,
  productName,
  available,
}: {
  storeProductId: string;
  productName: string;
  available: number;
}) {
  const { t } = useTranslation(["catalog", "validation"]);
  const { cart, error, mutate, pending } = useCart();
  const maximum = Math.min(Math.max(available, 0), 99);
  const [quantity, setQuantity] = useState("1");
  const [invalid, setInvalid] = useState(false);
  const [result, setResult] = useState<Result>(null);
  const submitting = useRef(false);
  const statusId = `add-to-cart-status-${storeProductId}`;
  const hintId = `add-to-cart-hint-${storeProductId}`;

  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current || pending || maximum === 0) return;

    const requested = Number(quantity);
    if (!Number.isInteger(requested) || requested < 1 || requested > maximum) {
      setInvalid(true);
      setResult(null);
      return;
    }

    submitting.current = true;
    setInvalid(false);
    setResult(null);
    const before =
      cart?.items.find((line) => line.storeProductId === storeProductId)
        ?.quantity ?? 0;
    const updated = await mutate(() => addToCart(storeProductId, requested));
    submitting.current = false;

    if (!updated) {
      setResult({ kind: "failed" });
      return;
    }

    const after =
      updated.items.find((line) => line.storeProductId === storeProductId)
        ?.quantity ?? 0;
    const actual = Math.max(after - before, 0);
    if (actual === 0) {
      setResult({ kind: "unchanged" });
      return;
    }

    setResult({ kind: "added", count: actual, capped: actual < requested });
  }

  return (
    <form className="add-to-cart" onSubmit={(event) => void add(event)} noValidate>
      <div className="quantity-field">
        <label htmlFor={`quantity-${storeProductId}`}>{t("catalog:quantity")}</label>
        <input
          id={`quantity-${storeProductId}`}
          name="quantity"
          type="number"
          inputMode="numeric"
          min="1"
          max={maximum || undefined}
          step="1"
          required
          value={quantity}
          disabled={pending || maximum === 0}
          aria-invalid={invalid || undefined}
          aria-describedby={`${hintId} ${statusId}`}
          onChange={(event) => {
            setQuantity(event.target.value);
            setInvalid(false);
            setResult(null);
          }}
        />
        <span id={hintId} className="hint">
          {maximum > 0
            ? t("catalog:quantityRange", { max: maximum })
            : t("catalog:outOfStock")}
        </span>
      </div>
      <Button
        type="submit"
        disabled={maximum === 0}
        aria-disabled={pending || undefined}
        aria-busy={pending || undefined}
        aria-label={t("catalog:addProductToCart", { name: productName })}
      >
        {t(pending ? "catalog:adding" : "catalog:addToCart")}
      </Button>
      <div id={statusId} className="add-to-cart-status" aria-live="polite" aria-atomic="true">
        {invalid && (
          <InlineMessage tone="error">
            {t("validation:quantityInvalid")}
          </InlineMessage>
        )}
        {result?.kind === "failed" &&
          (error !== null ? (
            <RequestError error={error} operation="write" />
          ) : (
            <InlineMessage tone="error">{t("catalog:addFailed")}</InlineMessage>
          ))}
        {result?.kind === "unchanged" && (
          <InlineMessage>{t("catalog:cartNotChanged")}</InlineMessage>
        )}
        {result?.kind === "added" && (
          <InlineMessage tone="success">
            {t(
              result.capped
                ? "catalog:addedToCartCapped"
                : "catalog:addedToCart",
              { count: result.count },
            )}{" "}
            <Link to="/cart">{t("catalog:viewCart")}</Link>
          </InlineMessage>
        )}
      </div>
    </form>
  );
}
