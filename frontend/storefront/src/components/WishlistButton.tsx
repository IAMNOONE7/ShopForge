import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { addToWishlist, getWishlist, removeFromWishlist } from "../account";
import { useCustomer } from "../customerContext";
import { RequestError } from "./ui/RequestError";

type WishlistState = {
  key: string;
  wanted: boolean | null;
  pending: boolean;
  error: unknown | null;
};

export function WishlistButton({ storeProductId }: { storeProductId: string }) {
  const { t } = useTranslation("catalog");
  const { customer } = useCustomer();
  const key = `${customer?.email ?? "guest"}:${storeProductId}`;
  const [result, setResult] = useState<WishlistState | null>(null);
  const lock = useRef(false);
  const state: WishlistState =
    result?.key === key
      ? result
      : { key, wanted: null, pending: false, error: null };

  useEffect(() => {
    const controller = new AbortController();
    if (customer) {
      getWishlist(controller.signal)
        .then((items) => {
          if (!controller.signal.aborted) {
            setResult({
              key,
              wanted: items.some(
                (item) => item.storeProductId === storeProductId,
              ),
              pending: false,
              error: null,
            });
          }
        })
        .catch((error: unknown) => {
          if (!controller.signal.aborted) {
            setResult({ key, wanted: null, pending: false, error });
          }
        });
    }
    return () => controller.abort();
  }, [customer?.email, key, storeProductId]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!customer || state.wanted === null) {
    return state.error !== null ? (
      <RequestError error={state.error} operation="read" />
    ) : null;
  }

  async function toggle() {
    if (lock.current || state.wanted === null) return;
    lock.current = true;
    const next = !state.wanted;
    setResult({ ...state, pending: true, error: null });
    try {
      await (next
        ? addToWishlist(storeProductId)
        : removeFromWishlist(storeProductId));
      setResult((current) =>
        current?.key === key
          ? { key, wanted: next, pending: false, error: null }
          : current,
      );
    } catch (error) {
      setResult((current) =>
        current?.key === key ? { ...state, pending: false, error } : current,
      );
    } finally {
      lock.current = false;
    }
  }

  return (
    <div className="product-wishlist">
      <button
        type="button"
        className="link-button"
        aria-pressed={state.wanted}
        disabled={state.pending}
        onClick={() => void toggle()}
      >
        {state.wanted ? t("wishlistOn") : t("wishlistOff")}
      </button>
      {state.error !== null && (
        <RequestError error={state.error} operation="write" />
      )}
    </div>
  );
}
