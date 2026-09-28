import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import {
  getOrders,
  getWishlist,
  removeFromWishlist,
  signOut,
  updateProfile,
  type WishlistItem,
} from "../account";
import { useCustomer } from "../customerContext";
import { formatPrice, useStore } from "../storeContext";
import { useRequest } from "../useRequest";
import { formatDate } from "../utils/format";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";

export function AccountPage() {
  const { t } = useTranslation([
    "account",
    "auth",
    "navigation",
    "orders",
    "common",
  ]);
  const store = useStore();
  const navigate = useNavigate();
  const {
    status: customerStatus,
    customer,
    error: customerError,
    apply,
    retry,
  } = useCustomer();
  const orders = useRequest(
    `account-orders:${customer?.email ?? "guest"}`,
    (signal) => (customer ? getOrders(signal) : Promise.resolve([])),
  );
  const [wishlistVersion, setWishlistVersion] = useState(0);
  const wishlist = useRequest(
    `account-wishlist:${customer?.email ?? "guest"}:${wishlistVersion}`,
    (signal) => (customer ? getWishlist(signal) : Promise.resolve([])),
  );
  const [saved, setSaved] = useState(false);
  const [actionError, setActionError] = useState<unknown | null>(null);
  const [pending, setPending] = useState(false);
  const actionLock = useRef(false);
  async function forget(item: WishlistItem) {
    await removeFromWishlist(item.storeProductId);
    setWishlistVersion((current) => current + 1);
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (actionLock.current) return;
    actionLock.current = true;
    setPending(true);
    setSaved(false);
    setActionError(null);
    const form = new FormData(event.currentTarget);
    try {
      const phone = String(form.get("phone")).trim();
      apply(
        await updateProfile({
          firstName: String(form.get("firstName")).trim(),
          lastName: String(form.get("lastName")).trim(),
          phone: phone || null,
        }),
      );
      setSaved(true);
    } catch (error) {
      setActionError(error);
    } finally {
      setPending(false);
      actionLock.current = false;
    }
  }
  async function leave() {
    await signOut();
    apply(null);
    void navigate("/");
  }
  if (customerStatus === "checking")
    return <LoadingState label={t("common:loading")} lines={4} />;
  if (customerStatus === "error")
    return (
      <RequestError error={customerError} operation="session" onRetry={retry} />
    );
  if (!customer)
    return (
      <section className="account-form">
        <h1>{t("account:title")}</h1>
        <p>
          <Link to="/account/sign-in">{t("auth:signIn")}</Link>{" "}
          {t("account:signInPromptSuffix")}
        </p>
      </section>
    );
  const history = orders.status === "ready" ? orders.data : [];
  return (
    <section className="account">
      <h1>{t("account:hello", { name: customer.firstName })}</h1>
      <p className="hint">
        {t("account:signedInAs", { email: customer.email })} ·{" "}
        <button
          type="button"
          className="link-button"
          onClick={() => void leave()}
        >
          {t("navigation:signOut")}
        </button>
      </p>
      <h2>{t("account:orders")}</h2>
      {orders.status === "error" && (
        <RequestError
          error={orders.error}
          operation="read"
          onRetry={orders.reload}
        />
      )}
      {orders.status === "ready" && orders.refreshError !== null && (
        <RequestError
          error={orders.refreshError}
          operation="read"
          onRetry={orders.reload}
        />
      )}
      {orders.status === "ready" && history.length === 0 && (
        <p className="hint">{t("account:noOrders")}</p>
      )}
      {history.length > 0 && (
        <table className="order-lines">
          <tbody>
            {history.map((order) => (
              <tr key={order.number}>
                <td>
                  <Link to={`/account/orders/${order.number}`}>
                    {order.number}
                  </Link>
                </td>
                <td>{formatDate(order.placedAt, store.culture)}</td>
                <td>{t(`orders:status.${knownStatus(order.status)}`)}</td>
                <td>{t("orders:itemCount", { count: order.items })}</td>
                <td className="order-amount">
                  {formatPrice(order.grandTotal, store)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <h2>{t("account:wishlist")}</h2>
      {wishlist.status === "error" && (
        <RequestError
          error={wishlist.error}
          operation="read"
          onRetry={wishlist.reload}
        />
      )}
      {wishlist.status === "ready" && wishlist.data.length === 0 && (
        <p className="hint">{t("account:wishlistEmpty")}</p>
      )}
      {wishlist.status === "ready" && wishlist.data.length > 0 && (
        <ul className="wishlist">
          {wishlist.data.map((item) => (
            <li key={item.storeProductId}>
              <Link to={`/p/${item.slug}`}>{item.name}</Link>
              <span>{formatPrice(item.price, store)}</span>
              <button
                type="button"
                className="link-button"
                onClick={() => void forget(item)}
              >
                {t("common:remove")}
              </button>
            </li>
          ))}
        </ul>
      )}
      <h2>{t("account:details")}</h2>
      <form
        onSubmit={(event) => void save(event)}
        className="account-form"
        aria-busy={pending}
      >
        <label>
          {t("auth:firstName")}{" "}
          <input name="firstName" defaultValue={customer.firstName} required />
        </label>
        <label>
          {t("auth:lastName")}{" "}
          <input name="lastName" defaultValue={customer.lastName} required />
        </label>
        <label>
          {t("auth:phone")}{" "}
          <input name="phone" type="tel" defaultValue={customer.phone ?? ""} />
        </label>
        <button type="submit" disabled={pending}>
          {t("common:save")}
        </button>
        {saved && <p className="hint">{t("account:saved")}</p>}
        {actionError !== null && (
          <RequestError error={actionError} operation="write" />
        )}
      </form>
    </section>
  );
}
function knownStatus(
  value: string,
):
  | "AwaitingPayment"
  | "Paid"
  | "Shipped"
  | "Cancelled"
  | "Refunded"
  | "unknown" {
  return [
    "AwaitingPayment",
    "Paid",
    "Shipped",
    "Cancelled",
    "Refunded",
  ].includes(value)
    ? (value as
        | "AwaitingPayment"
        | "Paid"
        | "Shipped"
        | "Cancelled"
        | "Refunded")
    : "unknown";
}
