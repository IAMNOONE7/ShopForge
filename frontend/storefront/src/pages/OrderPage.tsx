import { useEffect } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Link, useLocation, useParams, useSearchParams } from "react-router";
import { downloadGuestDocument } from "../api/documents";
import { getOrder } from "../cart";
import { clearCheckoutRecovery, orderPath, readCheckoutRecovery } from "../checkoutRecovery";
import { OrderReceipt } from "../components/orders/OrderReceipt";
import { useOrderRefresh } from "../components/orders/useOrderRefresh";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useStore } from "../storeContext";

export function OrderPage() {
  const { t } = useTranslation(["orders", "catalog"]);
  const { number = "" } = useParams();
  const [parameters] = useSearchParams();
  const token = parameters.get("token") ?? "";
  const location = useLocation();
  const suppliedInstructions = (
    location.state &&
    typeof location.state === "object" &&
    "instructions" in location.state &&
    typeof location.state.instructions === "string"
      ? location.state.instructions
      : undefined
  );
  const instructions = suppliedInstructions?.trim() || undefined;

  if (!number.trim() || !isOrderToken(token)) {
    return (
      <EmptyState
        title={t("orders:invalidLinkTitle")}
        action={
          <Link to="/" className="button">
            {t("catalog:browseAll")}
          </Link>
        }
      >
        <p>{t("orders:invalidLink")}</p>
      </EmptyState>
    );
  }

  return (
    <GuestOrder
      number={number}
      token={token}
      instructions={instructions}
    />
  );
}

function GuestOrder({
  number,
  token,
  instructions,
}: {
  number: string;
  token: string;
  instructions?: string;
}) {
  const { t } = useTranslation(["orders", "catalog"]);
  const store = useStore();
  const { request, waiting, timedOut, refresh } = useOrderRefresh(
    "guest-order:" + number + ":" + token,
    (signal) => getOrder(number, token, signal),
  );
  const data = request.status === "ready" ? request.data : null;

  useEffect(() => {
    if (!data) return;
    const recovery = readCheckoutRecovery();
    const currentPath = orderPath({ number, token });
    if (recovery?.orderPath === currentPath) clearCheckoutRecovery();
  }, [data, number, token]);

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
          <Link to="/" className="button">
            {t("catalog:browseAll")}
          </Link>
        }
      >
        <p>{t("orders:guestNotFound")}</p>
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
      heading={t("orders:thankYou")}
      introduction={
        <p>
          <Trans
            ns="orders"
            i18nKey="placed"
            values={{
              number: request.data.number,
              email: request.data.email,
            }}
            components={{ strong: <strong /> }}
          />
        </p>
      }
      instructions={instructions}
      refresh={{
        waiting,
        refreshing: request.refreshing,
        error: request.refreshError,
        timedOut,
        onRefresh: refresh,
      }}
      downloadDocument={(document) =>
        downloadGuestDocument(request.data.number, document.number, token)
      }
    />
  );
}

function isOrderToken(value: string) {
  return /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(
    value,
  );
}
