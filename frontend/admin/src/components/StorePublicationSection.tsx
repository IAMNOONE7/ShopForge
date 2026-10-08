import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api, type AdminStore } from "../api";
import { useAction } from "../useAction";
import { useRequest, type RequestState } from "../useRequest";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

type CheckStatus = "ready" | "missing" | "unknown";
type CheckKey = "logo" | "company" | "product" | "payment" | "shipping";
type Confirmation = "publish" | "unpublish" | null;

export function StorePublicationSection({
  store,
  reloadStores,
}: {
  store: AdminStore;
  reloadStores: () => void;
}) {
  const { t } = useTranslation("stores");
  const actionButton = useRef<HTMLButtonElement>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);
  const [confirmation, setConfirmation] = useState<Confirmation>(null);
  const [success, setSuccess] = useState<"published" | "unpublished" | null>(
    null,
  );
  const [products, reloadProducts] = useRequest(
    `publish-products:${store.id}`,
    (signal) => api.hasVisibleStoreProduct(store.id, signal),
  );
  const [payments, reloadPayments] = useRequest(
    `publish-payments:${store.id}`,
    (signal) => api.paymentMethods(store.id, signal),
  );
  const [shipping, reloadShipping] = useRequest(
    `publish-shipping:${store.id}`,
    (signal) => api.shippingMethods(store.id, signal),
  );
  const [error, run, pending] = useAction(() => {
    reloadStores();
    reloadProducts();
    reloadPayments();
    reloadShipping();
  });

  useEffect(() => {
    if (confirmation) confirmButton.current?.focus();
  }, [confirmation]);

  const checks: {
    key: CheckKey;
    status: CheckStatus;
    href: string | null;
  }[] = [
    {
      key: "logo",
      status: store.logoUrl ? "ready" : "missing",
      href: null,
    },
    {
      key: "company",
      status: store.company ? "ready" : "missing",
      href: null,
    },
    {
      key: "product",
      status: requestStatus(products, (visible) => visible),
      href: `/stores/${store.id}/products`,
    },
    {
      key: "payment",
      status: requestStatus(payments, (items) =>
        items.some((item) => item.isActive),
      ),
      href: `/stores/${store.id}/methods`,
    },
    {
      key: "shipping",
      status: requestStatus(shipping, (items) =>
        items.some((item) => item.isActive),
      ),
      href: `/stores/${store.id}/methods`,
    },
  ];

  const readsFailed =
    hasReadError(products) ||
    hasReadError(payments) ||
    hasReadError(shipping);
  const refreshing = [products, payments, shipping].some(
    (state) =>
      state.status === "loading" ||
      (state.status === "ready" && state.refreshing),
  );

  function closeConfirmation() {
    setConfirmation(null);
    window.requestAnimationFrame(() => actionButton.current?.focus());
  }

  async function changePublication() {
    if (!confirmation) return;
    const action = confirmation;
    let succeeded = false;
    setSuccess(null);
    await run(async () => {
      if (action === "publish") await api.publishStore(store.id);
      else await api.unpublishStore(store.id);
      succeeded = true;
      setSuccess(action === "publish" ? "published" : "unpublished");
    });
    if (succeeded) closeConfirmation();
  }

  function retryChecks() {
    reloadProducts();
    reloadPayments();
    reloadShipping();
  }

  return (
    <section
      className="store-settings-panel publication-panel"
      aria-labelledby="publication-title"
    >
      <div className="section-heading">
        <div>
          <h2 id="publication-title">{t("publication")}</h2>
          <p className="hint">
            {t(
              store.status === "draft"
                ? "publicationDraftHint"
                : "publicationLiveHint",
            )}
          </p>
        </div>
        <span className={`publication-state publication-state-${store.status}`}>
          {t(`status.${store.status}`)}
        </span>
      </div>

      <div className="publication-layout">
        <div>
          <h3>{t("readinessTitle")}</h3>
          <p className="hint">{t("readinessHint")}</p>
          <ul className="readiness-list" aria-busy={refreshing || undefined}>
            {checks.map((check) => (
              <li key={check.key}>
                <span
                  className={`readiness-icon readiness-${check.status}`}
                  aria-hidden="true"
                >
                  {check.status === "ready"
                    ? "✓"
                    : check.status === "missing"
                      ? "!"
                      : "?"}
                </span>
                <span>
                  <strong>{t(`checks.${check.key}`)}</strong>
                  <small>{t(`checkStatus.${check.status}`)}</small>
                </span>
                {check.href && check.status === "missing" && (
                  <Link to={check.href}>{t("fixCheck")}</Link>
                )}
              </li>
            ))}
          </ul>
          {readsFailed && (
            <div className="check-retry">
              <InlineMessage tone="info">{t("checksUnavailable")}</InlineMessage>
              <Button
                type="button"
                variant="secondary"
                data-read-action
                onClick={retryChecks}
              >
                {t("retryChecks")}
              </Button>
            </div>
          )}
        </div>

        <div className="publication-action">
          {!confirmation && (
            <>
              <Button
                ref={actionButton}
                type="button"
                variant={store.status === "published" ? "danger" : "primary"}
                onClick={() => {
                  setSuccess(null);
                  setConfirmation(
                    store.status === "draft" ? "publish" : "unpublish",
                  );
                }}
              >
                {t(
                  store.status === "draft"
                    ? "reviewPublication"
                    : "reviewUnpublish",
                )}
              </Button>
              <p className="hint">
                {t(
                  store.status === "draft"
                    ? "backendFinalHint"
                    : "unpublishWarning",
                )}
              </p>
            </>
          )}
          {confirmation && (
            <div className="publication-confirmation" role="group">
              <strong>
                {t(
                  confirmation === "publish"
                    ? "confirmPublishTitle"
                    : "confirmUnpublishTitle",
                  { name: store.name },
                )}
              </strong>
              <p>
                {t(
                  confirmation === "publish"
                    ? "confirmPublishBody"
                    : "confirmUnpublishBody",
                )}
              </p>
              <div className="cluster">
                <Button
                  ref={confirmButton}
                  type="button"
                  variant={confirmation === "unpublish" ? "danger" : "primary"}
                  busy={pending}
                  busyLabel={t("updatingPublication")}
                  onClick={() => void changePublication()}
                >
                  {t(
                    confirmation === "publish"
                      ? "confirmPublish"
                      : "confirmUnpublish",
                  )}
                </Button>
                <Button
                  type="button"
                  variant="secondary"
                  disabled={pending}
                  onClick={closeConfirmation}
                >
                  {t("cancel")}
                </Button>
              </div>
            </div>
          )}
        </div>
      </div>

      {success && (
        <InlineMessage tone="success">
          {t(
            success === "published"
              ? "publishedSuccess"
              : "unpublishedSuccess",
            { name: store.name },
          )}
        </InlineMessage>
      )}
      {error !== null && (
        <>
          <RequestError error={error} operation="write" />
          <p className="hint">{t("publicationFailureHint")}</p>
        </>
      )}
    </section>
  );
}

function requestStatus<T>(
  state: RequestState<T>,
  ready: (items: T) => boolean,
): CheckStatus {
  if (state.status !== "ready" || state.refreshError) return "unknown";
  return ready(state.data) ? "ready" : "missing";
}

function hasReadError<T>(state: RequestState<T>) {
  return (
    state.status === "error" ||
    state.status === "not-found" ||
    (state.status === "ready" && state.refreshError !== null)
  );
}
