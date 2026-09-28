import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { applyDiscount, removeDiscount } from "../cart";
import { useCart } from "../cartContext";
import { formatPrice, useStore } from "../storeContext";

export function DiscountField() {
  const { t } = useTranslation("cart");
  const store = useStore();
  const { cart, mutate, pending } = useCart();
  const [problem, setProblem] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setProblem(false);
    const form = new FormData(event.currentTarget);
    const result = await mutate(() =>
      applyDiscount(String(form.get("code")).trim()),
    );
    setProblem(result === null);
  }
  async function remove() {
    setProblem(false);
    const result = await mutate(removeDiscount);
    setProblem(result === null);
  }
  if (!cart) return null;
  if (cart.discount)
    return (
      <p className="discount applied">
        <span>
          {cart.discount.name} (<strong>{cart.discount.code}</strong>) −
          {formatPrice(cart.discount.amount, store)}
        </span>
        <button
          type="button"
          className="link-button"
          disabled={pending}
          onClick={() => void remove()}
        >
          {t("discountRemove")}
        </button>
        {problem && <span className="error">{t("discountFailed")}</span>}
      </p>
    );
  const failed = problem || Boolean(cart.discountProblem);
  return (
    <form
      onSubmit={(event) => void submit(event)}
      className="discount"
      aria-busy={pending}
    >
      <label>
        <span className="visually-hidden">{t("discountCode")}</span>
        <input
          name="code"
          placeholder={t("discountCode")}
          autoComplete="off"
          required
        />
      </label>
      <button type="submit" disabled={pending}>
        {t("discountApply")}
      </button>
      {failed && <span className="error">{t("discountFailed")}</span>}
      {cart.discountProblem && !problem && (
        <button
          type="button"
          className="link-button"
          disabled={pending}
          onClick={() => void remove()}
        >
          {t("discountRemove")}
        </button>
      )}
    </form>
  );
}
