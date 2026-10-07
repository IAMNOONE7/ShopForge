import { useId, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { applyDiscount, removeDiscount } from "../cart";
import { useCart } from "../cartContext";
import { useStore } from "../storeContext";

export function DiscountField() {
  const { t } = useTranslation("cart");
  const store = useStore();
  const { cart, mutate, pending } = useCart();
  const [code, setCode] = useState("");
  const [problem, setProblem] = useState<"apply" | "remove" | null>(null);
  const [announcement, setAnnouncement] = useState("");
  const [action, setAction] = useState<"apply" | "remove" | null>(null);
  const locked = useRef(false);
  const status = useId();
  const busy = pending || action !== null;

  async function change(kind: "apply" | "remove") {
    if (locked.current || pending) return;
    if (kind === "apply" && !code.trim()) { setProblem("apply"); return; }
    locked.current = true;
    setProblem(null);
    setAnnouncement("");
    setAction(kind);
    try {
      const result = await mutate(kind === "apply" ? () => applyDiscount(code.trim()) : removeDiscount);
      setProblem(result === null ? kind : null);
      if (result) {
        setCode("");
        setAnnouncement(kind === "remove" ? t("discountRemoved") : result.discount ? t("discountApplied", { code: result.discount.code }) : "");
      }
    } finally {
      locked.current = false;
      setAction(null);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void change("apply");
  }
  if (!cart) return null;
  const failed = problem !== null || Boolean(cart.discountProblem);
  return (
    <section className="cart-discount" aria-label={t("discountCode")}>
      {cart.discount ? (
        <div className="discount applied">
          <div><span>{t("codeApplied")}</span><strong><bdi lang={store.culture}>{cart.discount.code}</bdi></strong></div>
          <button type="button" className="link-button" disabled={busy} onClick={() => void change("remove")}>
            {action === "remove" ? t("removing") : t("discountRemove")}
          </button>
        </div>
      ) : (
        <form onSubmit={submit} className="discount" aria-busy={busy || undefined} noValidate>
          <label>
            <span>{t("discountCode")}</span>
            <input name="code" value={code} autoComplete="off" required readOnly={busy}
              aria-invalid={failed || undefined} aria-describedby={status}
              onChange={(event) => { setCode(event.target.value); setProblem(null); setAnnouncement(""); }} />
          </label>
          <button type="submit" disabled={busy}>{action === "apply" ? t("discountApplying") : t("discountApply")}</button>
          {cart.discountProblem && <button type="button" className="link-button" disabled={busy} onClick={() => void change("remove")}>{t("discountRemove")}</button>}
        </form>
      )}
      <div id={status} className="discount-status" aria-live="polite" aria-atomic="true">
        {failed && <p className="error">{t(problem === "remove" ? "discountRemoveFailed" : "discountFailed")}</p>}
        {announcement && <span className="sr-only">{announcement}</span>}
      </div>
    </section>
  );
}
