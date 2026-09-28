import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import {
  removeFromCart,
  setCartQuantity,
  type Cart,
  type CartLine as CartLineModel,
} from "../../cart";
import { useCart } from "../../cartContext";
import { formatPrice, useStore } from "../../storeContext";
import { InlineMessage } from "../ui/InlineMessage";

type LineAction = "idle" | "updating" | "removing" | "failed";

export function CartLine({
  line,
  index,
  onRemoved,
}: {
  line: CartLineModel;
  index: number;
  onRemoved: (cart: Cart, index: number, name: string) => void;
}) {
  const { t } = useTranslation(["cart", "validation"]);
  const store = useStore();
  const { mutate, pending } = useCart();
  const [quantity, setQuantity] = useState(String(line.quantity));
  const [invalid, setInvalid] = useState(false);
  const [action, setAction] = useState<LineAction>("idle");
  const [message, setMessage] = useState<string | null>(null);
  const lock = useRef(false);
  const [imageFailed, setImageFailed] = useState(false);
  const maximum = Math.min(line.available, 99);
  const busy =
    pending || action === "updating" || action === "removing";

  async function update(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current || pending) return;
    const requested = Number(quantity);
    if (
      !Number.isInteger(requested) ||
      requested < 1 ||
      requested > maximum
    ) {
      setInvalid(true);
      setMessage(null);
      return;
    }

    lock.current = true;
    setInvalid(false);
    setMessage(null);
    setAction("updating");
    const result = await mutate(() =>
      setCartQuantity(line.storeProductId, requested),
    );
    lock.current = false;

    if (!result) {
      setAction("failed");
      return;
    }

    const updated = result.items.find(
      (item) => item.storeProductId === line.storeProductId,
    );
    if (!updated) {
      onRemoved(result, index, line.name);
      return;
    }

    setQuantity(String(updated.quantity));
    setAction("idle");
    setMessage(
      t(
        updated.quantity === requested
          ? "cart:quantityUpdated"
          : "cart:quantityAdjusted",
        { name: line.name, count: updated.quantity },
      ),
    );
  }

  async function remove() {
    if (lock.current || pending) return;
    lock.current = true;
    setMessage(null);
    setAction("removing");
    const result = await mutate(() => removeFromCart(line.storeProductId));
    lock.current = false;

    if (!result) {
      setAction("failed");
      return;
    }
    onRemoved(result, index, line.name);
  }

  return (
    <li
      className={`cart-line ${action === "removing" ? "removing" : ""}`}
      aria-busy={action === "updating" || action === "removing" || undefined}
    >
      {line.imageUrl && !imageFailed ? (
        <img
          src={line.imageUrl}
          alt=""
          width="96"
          height="72"
          className="cart-thumbnail"
          onError={() => setImageFailed(true)}
        />
      ) : (
        <div className="cart-thumbnail cart-thumbnail-placeholder" aria-hidden="true" />
      )}
      <div className="cart-line-identity">
        <Link
          id={`cart-line-link-${line.storeProductId}`}
          to={`/p/${line.slug}`}
          className="cart-line-name"
          lang={store.culture}
        >
          {line.name}
        </Link>
        <span className="cart-unit-price">
          {t("cart:unitPrice", { price: formatPrice(line.unitPrice, store) })}
        </span>
      </div>
      <form
        className="cart-line-quantity"
        onSubmit={(event) => void update(event)}
        noValidate
      >
        <label htmlFor={`cart-quantity-${line.storeProductId}`}>
          <span>{t("cart:quantityLabel")}</span>
          <input
            id={`cart-quantity-${line.storeProductId}`}
            name="quantity"
            type="number"
            inputMode="numeric"
            min="1"
            max={maximum}
            step="1"
            required
            value={quantity}
            readOnly={pending}
            aria-disabled={pending || undefined}
            aria-invalid={invalid || undefined}
            aria-label={t("cart:quantity", { name: line.name })}
            aria-describedby={`cart-line-status-${line.storeProductId}`}
            onChange={(event) => {
              setQuantity(event.target.value);
              setInvalid(false);
              setAction("idle");
              setMessage(null);
            }}
          />
        </label>
        <button
          type="submit"
          aria-disabled={busy || undefined}
          aria-label={t("cart:updateQuantity", { name: line.name })}
        >
          {action === "updating" ? t("cart:updating") : t("cart:update")}
        </button>
      </form>
      <div className="cart-line-total">
        <span>{t("cart:lineTotal")}</span>
        <strong>{formatPrice(line.lineTotal, store)}</strong>
      </div>
      <button
        type="button"
        className="link-button cart-remove"
        aria-disabled={busy || undefined}
        aria-label={t("cart:removeItem", { name: line.name })}
        onClick={() => void remove()}
      >
        {action === "removing" ? t("cart:removing") : t("cart:remove")}
      </button>
      <div
        id={`cart-line-status-${line.storeProductId}`}
        className="cart-line-status"
        aria-live="polite"
        aria-atomic="true"
      >
        {invalid && (
          <InlineMessage tone="error">
            {t("validation:quantityInvalid")}
          </InlineMessage>
        )}
        {action === "failed" && (
          <InlineMessage tone="error">{t("cart:changeFailed")}</InlineMessage>
        )}
        {message && <InlineMessage tone="success">{message}</InlineMessage>}
      </div>
    </li>
  );
}
