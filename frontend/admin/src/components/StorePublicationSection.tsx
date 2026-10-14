import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { api, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import { publicationRefusal } from "../api/readiness";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { StoreReadinessList } from "./StoreReadinessList";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

type Props = {
  store: AdminStore;
  reloadStores: () => void;
  canManage: boolean;
  storeRefreshing?: boolean;
  storeRefreshError?: unknown | null;
};
type Confirmation = "publish" | "unpublish" | null;

export function StorePublicationSection(props: Props) {
  return <StorePublication key={props.store.id} {...props} />;
}

function StorePublication({ store, reloadStores, canManage, storeRefreshing = false, storeRefreshError = null }: Props) {
  const { t } = useTranslation("stores");
  const heading = useRef<HTMLHeadingElement>(null);
  const actionButton = useRef<HTMLButtonElement>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);
  const failureNotice = useRef<HTMLDivElement>(null);
  const active = useRef(true);
  const previousStore = useRef(store);
  const readVersion = useRef(0);
  const [confirmation, setConfirmation] = useState<Confirmation>(null);
  const [reportedStore, setReportedStore] = useState<AdminStore | null>(null);
  const [success, setSuccess] = useState<"published" | "unpublished" | null>(null);
  const [refusal, setRefusal] = useState<(NonNullable<ReturnType<typeof publicationRefusal>> & { version: number }) | null>(null);
  const [readiness, reloadReadiness] = useRequest(`readiness:${store.id}`, async (signal) => {
    const version = ++readVersion.current;
    return { snapshot: await api.storeReadiness(store.id, signal), version, store };
  });
  // React owns disabled state here because a refreshed prerequisite can keep a completed action blocked.
  const [error, run, pending] = useAction(() => { reloadStores(); reloadReadiness(); }, { manageControls: false });

  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => {
    if (previousStore.current === store) return;
    previousStore.current = store;
    setReportedStore(null); setSuccess(null); setConfirmation(null);
    reloadReadiness();
  }, [store, reloadReadiness]);
  useEffect(() => { if (confirmation) confirmButton.current?.focus(); }, [confirmation]);

  const snapshot = readiness.status === "ready" ? readiness.data.snapshot : null;
  const refusalActive = refusal !== null && (readiness.status !== "ready" || readiness.data.version <= refusal.version);
  const checks = refusalActive && refusal.checks !== null ? refusal.checks : snapshot?.checks;
  const refreshing = readiness.status === "loading" || (readiness.status === "ready" && readiness.refreshing);
  const readError = readiness.status === "error" || readiness.status === "not-found" ? readiness.error
    : readiness.status === "ready" ? readiness.refreshError : null;
  const readinessOutdated = readiness.status === "ready" && readiness.data.store !== store;
  const stale = refreshing || readError !== null || readinessOutdated;
  const storeStale = storeRefreshing || storeRefreshError !== null;
  const ready = snapshot?.isReady === true && !refusalActive && !stale;
  const status = (reportedStore ?? store).status;
  const actionDisabled = pending || storeStale || (status === "draft" && !ready);

  function closeConfirmation() {
    setConfirmation(null);
    window.requestAnimationFrame(() => {
      if (actionButton.current && !actionButton.current.disabled) actionButton.current.focus();
      else heading.current?.focus();
    });
  }

  async function changePublication() {
    if (!canManage || !confirmation || pending || storeStale || (confirmation === "publish" && !ready)) return;
    const action = confirmation;
    setSuccess(null); setRefusal(null);
    let succeeded = false;
    await run(async () => {
      try {
        const updated = action === "publish" ? await api.publishStore(store.id) : await api.unpublishStore(store.id);
        if (updated.id !== store.id || updated.status !== (action === "publish" ? "published" : "draft")) {
          throw new HttpError("invalid-response", 200);
        }
        if (!active.current) return;
        setReportedStore(updated);
        setSuccess(action === "publish" ? "published" : "unpublished");
        succeeded = true;
      } catch (failure) {
        if (active.current) {
          const response = publicationRefusal(failure);
          if (response) setRefusal({ ...response, version: readVersion.current });
          reloadReadiness(); reloadStores();
        }
        throw failure;
      }
    });
    if (!active.current) return;
    if (succeeded) closeConfirmation();
    else window.requestAnimationFrame(() => failureNotice.current?.focus());
  }

  return <section className="store-settings-panel publication-panel" aria-labelledby="publication-title">
    <div className="section-heading">
      <div><h2 id="publication-title" ref={heading} tabIndex={-1}>{t("publication")}</h2>
        <p className="hint">{t(status === "draft" ? "publicationDraftHint" : "publicationLiveHint")}</p>
      </div>
      <span className={`publication-state publication-state-${status}`}>{t(`status.${status}`)}</span>
    </div>
    <div className="publication-layout">
      <div className="publication-readiness" aria-busy={refreshing || undefined}>
        <div className="readiness-heading"><h3>{t("readinessTitle")}</h3>
          <Button type="button" variant="secondary" data-read-action disabled={pending || refreshing} onClick={reloadReadiness}>{t("retryChecks")}</Button>
        </div>
        <p className="hint">{t("readinessHint")}</p>
        <InlineMessage tone={stale ? "info" : ready ? "success" : "info"}>
          {t(stale ? "checksUnavailable" : ready ? "readinessReady" : "readinessBlocked")}
        </InlineMessage>
        {status === "published" && !ready && !stale && <InlineMessage tone="error">{t("publishedDegraded")}</InlineMessage>}
        {refreshing && <p role="status" className="hint">{t("readinessRefreshing")}</p>}
        {storeStale && <p className="hint">{t("publicationStoreStale")}</p>}
        {readError !== null && <RequestError error={readError} operation="read" onRetry={reloadReadiness} />}
        {readiness.status === "loading" && !checks && <LoadingState label={t("readinessRefreshing")} lines={3} />}
        {refusalActive && <p className="hint">{t("readinessRefused")}</p>}
        {checks && <StoreReadinessList storeId={store.id} checks={checks} stale={stale} />}
        {refusalActive && refusal.checks === null && refusal.problems.length > 0 && <div className="readiness-refusal">
          <strong>{t("readinessServerDetail")}</strong><ul>{refusal.problems.map((problem, index) => <li key={index}>{problem}</li>)}</ul>
        </div>}
      </div>
      {canManage && <div className="publication-action">
        {!confirmation ? <>
          <Button ref={actionButton} type="button" variant={status === "published" ? "danger" : "primary"} disabled={actionDisabled}
            onClick={() => { if (actionDisabled) return; setSuccess(null); setConfirmation(status === "draft" ? "publish" : "unpublish"); }}>
            {t(status === "draft" ? "reviewPublication" : "reviewUnpublish")}
          </Button>
          <p className="hint">{t(status === "draft" ? "backendFinalHint" : "unpublishWarning")}</p>
        </> : <div className="publication-confirmation" role="group">
          <strong>{t(confirmation === "publish" ? "confirmPublishTitle" : "confirmUnpublishTitle", { name: store.name })}</strong>
          <p>{t(confirmation === "publish" ? "confirmPublishBody" : "confirmUnpublishBody")}</p>
          <div className="cluster">
            <Button ref={confirmButton} type="button" variant={confirmation === "unpublish" ? "danger" : "primary"} busy={pending}
              disabled={pending || storeStale || (confirmation === "publish" && !ready)} busyLabel={t("updatingPublication")} onClick={() => void changePublication()}>
              {t(confirmation === "publish" ? "confirmPublish" : "confirmUnpublish")}
            </Button>
            <Button type="button" variant="secondary" disabled={pending} onClick={closeConfirmation}>{t("cancel")}</Button>
          </div>
        </div>}
      </div>}
    </div>
    {success && <InlineMessage tone="success">{t(success === "published" ? "publishedSuccess" : "unpublishedSuccess", { name: store.name })}</InlineMessage>}
    {error !== null && <div ref={failureNotice} tabIndex={-1}>
      <RequestError error={error} operation="write" />
      <p className="hint">{t("publicationFailureHint")}</p>
    </div>}
  </section>;
}
