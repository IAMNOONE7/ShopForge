import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { getAccountOrder } from "../account";
import { downloadAccountDocument } from "../api/documents";
import { authPath } from "../auth";
import { OrderReturns } from "../components/OrderReturns";
import { OrderReceipt } from "../components/orders/OrderReceipt";
import { useOrderRefresh } from "../components/orders/useOrderRefresh";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";
import { useStore } from "../storeContext";

export function AccountOrderPage() {
  const { t } = useTranslation(["orders", "account", "auth"]);
  const { number = "" } = useParams();
  const customerState = useCustomer();

  if (customerState.status === "checking") {
    return (
      <section className="order-load-state">
        <h1>{t("orders:orderTitle", { number })}</h1>
        <LoadingState label={t("orders:loading")} lines={7} />
      </section>
    );
  }

  if (customerState.status === "error") {
    return (
      <section className="order-load-state">
        <h1>{t("orders:orderTitle", { number })}</h1>
        <RequestError
          error={customerState.error}
          operation="session"
          onRetry={customerState.retry}
        />
      </section>
    );
  }

  if (customerState.status === "guest" || !customerState.customer) {
    const returnTo = `/account/orders/${encodeURIComponent(number)}`;
    return (
      <EmptyState
        title={t("account:privateOrderTitle")}
        action={
          <Link
            to={authPath("/account/sign-in", returnTo)}
            className="button"
          >
            {t("auth:signIn")}
          </Link>
        }
      >
        <p>{t("account:privateOrderBody")}</p>
      </EmptyState>
    );
  }

  return <AuthenticatedAccountOrder number={number} />;
}

function AuthenticatedAccountOrder({ number }: { number: string }) {
  const { t } = useTranslation(["orders", "account"]);
  const store = useStore();
  const { request, waiting, timedOut, refresh } = useOrderRefresh(
    "account-order:" + number,
    (signal) => getAccountOrder(number, signal),
  );

  if (request.status === "loading") {
    return (
      <section className="order-load-state">
        <h1>{t("orders:orderTitle", { number })}</h1>
        <LoadingState label={t("orders:loading")} lines={7} />
      </section>
    );
  }

  if (request.status === "not-found") {
    return (
      <EmptyState
        title={t("orders:notFoundTitle")}
        action={
          <Link to="/account" className="button">
            {t("orders:backToAccount")}
          </Link>
        }
      >
        <p>{t("orders:accountNotFound")}</p>
      </EmptyState>
    );
  }

  if (request.status === "error") {
    return (
      <section className="order-load-state">
        <h1>{t("orders:orderTitle", { number })}</h1>
        <RequestError
          error={request.error}
          operation="read"
          onRetry={refresh}
        />
      </section>
    );
  }

  return (
    <OrderReceipt
      order={request.data}
      store={store}
      heading={t("orders:orderTitle", { number: request.data.number })}
      introduction={
        <p>
          <Link to="/account">{t("orders:backToAccount")}</Link>
        </p>
      }
      refresh={{
        waiting,
        refreshing: request.refreshing,
        error: request.refreshError,
        timedOut,
        onRefresh: refresh,
      }}
      downloadDocument={(document) =>
        downloadAccountDocument(request.data.number, document.number)
      }
    >
      <OrderReturns
        key={request.data.number}
        number={request.data.number}
        orderStatus={request.data.status}
        currency={request.data.currency}
        onReturned={refresh}
      />
    </OrderReceipt>
  );
}
