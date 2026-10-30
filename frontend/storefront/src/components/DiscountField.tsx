import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { isHttpError } from "../api/http";
import { applyDiscount, removeDiscount, type Cart } from "../cart";
import { useCart } from "../cartContext";
import { useStore } from "../storeContext";

export function DiscountField({ id, removalOnly = false, disabled = false, onRemoved, onRemovalFailed }: {
  id?: string;
  removalOnly?: boolean;
  disabled?: boolean;
  onRemoved?: () => void;
  onRemovalFailed?: () => void;
}) {
  const { t } = useTranslation("cart");
  const store = useStore();
  const { cart, mutate, pending, error, reload } = useCart();
  const [code, setCode] = useState("");
  const [problem, setProblem] = useState<"apply" | "remove" | null>(null);
  const [announcement, setAnnouncement] = useState("");
  const [action, setAction] = useState<"apply" | "remove" | null>(null);
  const locked = useRef(false), active = useRef(false), root = useRef<HTMLElement>(null);
  const [readNeeded, setReadNeeded] = useState<Cart | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const status = useId();
  const removeBlocked = readNeeded !== null && (cart === readNeeded || error !== null);
  const busy = pending || action !== null || disabled || removeBlocked;

  async function change(kind: "apply" | "remove") {
    if (locked.current || busy || kind === "apply" && removalOnly) return;
    if (kind === "apply" && !code.trim()) { setProblem("apply"); return; }
    locked.current = true;
    setProblem(null);
    setAnnouncement("");
    setAction(kind);
    try {
      const result = await mutate(kind === "apply" ? () => applyDiscount(code.trim()) : removeDiscount);
      if (!active.current) return;
      setProblem(result === null ? kind : null);
      if (kind === "remove" && result === null) { setReadNeeded(cart); onRemovalFailed?.(); }
      if (result) {
        setReadNeeded(null); setCode("");
        if (kind === "remove") {
          onRemoved?.();
          if (!removalOnly) window.requestAnimationFrame(() => { if (active.current) root.current?.querySelector<HTMLInputElement>('[name="code"]')?.focus(); });
        }
        setAnnouncement(kind === "remove" ? t("discountRemoved") : result.discount ? t("discountApplied", { code: result.discount.code }) : "");
      }
    } finally {
      locked.current = false;
      if (active.current) setAction(null);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void change("apply");
  }
  if (!cart || removalOnly && !cart.discount && !cart.discountProblem) return null;
  const fieldMessages = problem === "apply" && isHttpError(error) && error.status === 400
    ? Object.entries(error.problem?.errors ?? {}).filter(([field]) => field.toLowerCase() === "code")
      .flatMap(([, values]) => Array.isArray(values) ? values.filter(value => typeof value === "string" && value.trim()) : []) : [];
  const failed = problem !== null || Boolean(cart.discountProblem);
  return (
    <section ref={root} id={id} tabIndex={id ? -1 : undefined} className="cart-discount" aria-label={t("discountCode")}>
      {removalOnly && <h2>{t("discountCode")}</h2>}
      {cart.discount ? (
        <div className="discount applied">
          <div><span>{t("codeApplied")}</span><strong><bdi lang={store.culture}>{cart.discount.code}</bdi></strong></div>
          <button type="button" className="link-button" disabled={busy} onClick={() => void change("remove")}>
            {action === "remove" ? t("removing") : t("discountRemove")}
          </button>
        </div>
      ) : removalOnly ? <div className="discount applied">
        <p>{t("discountNeedsRemoval")}</p>
        <button type="button" className="link-button" disabled={busy} onClick={() => void change("remove")}>{action === "remove" ? t("removing") : t("discountRemove")}</button>
      </div> : (
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
        {fieldMessages.map((message, index) => <p key={index} className="error">{message}</p>)}
        {cart.discountProblem && <p className="error">{cart.discountProblem}</p>}
        {failed && fieldMessages.length === 0 && (!cart.discountProblem || problem === "remove") && <p className="error">{t(problem === "remove" ? "discountRemoveFailed" : "discountFailed")}</p>}
        {removeBlocked && !onRemovalFailed && <button type="button" disabled={pending || disabled} onClick={reload}>{t("discountRefreshCart")}</button>}
        {announcement && <span className="sr-only">{announcement}</span>}
      </div>
    </section>
  );
}
