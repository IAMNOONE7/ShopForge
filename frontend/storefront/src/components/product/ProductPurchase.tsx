import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { ProductDetail } from "../../api";
import { useCart } from "../../cartContext";
import { useStore } from "../../storeContext";
import { AddToCart } from "../AddToCart";
import { WishlistButton } from "../WishlistButton";

export function ProductPurchase({ product }: { product: ProductDetail }) {
  const { t } = useTranslation("catalog");
  const store = useStore();
  const { pending } = useCart();
  const [selected, setSelected] = useState<(string | null)[]>(() =>
    product.optionNames.map(() => null),
  );
  const [clearedChoices, setClearedChoices] = useState(false);
  const needsChoice = product.variants.length > 1;
  const optionKeys = product.variants.map((candidate) => JSON.stringify(candidate.optionValues));
  const optionsUsable = !needsChoice || (
    product.optionNames.length > 0 &&
    product.variants.every((candidate) => candidate.optionValues.length === product.optionNames.length) &&
    new Set(optionKeys).size === optionKeys.length
  );
  const variant = needsChoice
    ? optionsUsable
      ? product.variants.find((candidate) =>
          candidate.optionValues.every((value, index) => selected[index] === value),
        )
      : undefined
    : product.variants[0];
  const available = variant?.available ?? (product.variants.length === 0 ? 0 : null);
  const availability =
    !optionsUsable
      ? t("variantOptionsUnavailable")
      : available === null
      ? t("chooseOptions")
      : available === 0
        ? t("outOfStock")
        : available <= 5
          ? t("onlyLeft", { count: available })
          : t("inStockCount", { count: available });

  function choose(index: number, value: string) {
    const next = product.optionNames.map((_, axis) =>
      axis === index ? value : selected[axis] ?? null,
    );
    const compatible = product.variants.some((candidate) =>
      candidate.optionValues.every((option, axis) =>
        next[axis] === null || next[axis] === option),
    );
    if (!compatible) {
      for (let axis = 0; axis < next.length; axis += 1) {
        if (axis !== index) next[axis] = null;
      }
    }
    setSelected(next);
    setClearedChoices(!compatible);
  }

  return (
    <section className="product-purchase" aria-label={t("purchaseOptions")}>
      {needsChoice && optionsUsable && (
        <div className="product-variant-picker">
          {product.optionNames.map((name, index) => {
            const values = [...new Set(product.variants.map((candidate) => candidate.optionValues[index]))];
            return (
              <fieldset key={index} className="variant-group" disabled={pending}>
                <legend lang={store.culture}>{name}</legend>
                <div className="variant-values">
                  {values.map((value) => {
                    const soldOut = product.variants
                      .filter((candidate) => candidate.optionValues[index] === value)
                      .every((candidate) => candidate.available === 0);
                    return (
                      <label key={value} className="variant-choice">
                        <input
                          type="radio"
                          name={"variant-option-" + index}
                          value={value}
                          checked={selected[index] === value}
                          onChange={() => choose(index, value)}
                        />
                        <span>
                          <bdi lang={store.culture}>{value}</bdi>{" "}
                          {soldOut && <small>{t("outOfStock")}</small>}
                        </span>
                      </label>
                    );
                  })}
                </div>
              </fieldset>
            );
          })}
          {clearedChoices && (
            <p className="variant-selection-note" role="status">{t("otherOptionsCleared")}</p>
          )}
        </div>
      )}
      {variant && variant.optionValues.length > 0 && (
        <p className="product-selected-options">
          <span>{t("selectedOptions")}</span>
          <bdi lang={store.culture}>{variant.optionValues.join(" / ")}</bdi>
        </p>
      )}
      <p
        className={"availability " + (available === null ? "pending" : available === 0 ? "unavailable" : "available")}
        aria-live="polite"
      >
        {availability}
      </p>
      <div className="product-actions">
        {optionsUsable && (
          <AddToCart
            key={product.id + ":" + (variant?.id ?? "none") + ":" + (available ?? "unselected")}
            storeProductId={product.id}
            variantId={variant?.id ?? null}
            productName={product.name}
            available={available}
          />
        )}
        <WishlistButton storeProductId={product.id} />
      </div>
    </section>
  );
}
