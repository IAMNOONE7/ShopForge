import { useEffect, useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { HttpError, isHttpError } from "../api/http";
import { offeredSetupActions, setupProgressApi, setupRecoverySupported, type SetupAction, type SetupDetail } from "../api/setupProgress";
import { canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import type { useSetupProgress } from "../useSetupProgress";
import { Button } from "./ui/Button";
import { RequestError } from "./ui/RequestError";

type Confirmation = { action: SetupAction; item: SetupDetail; fingerprint: string; storeName: string };
type Outcome = "accepted" | "refused" | "uncertain";
export function SetupRecoveryPanel({ read, item, renderKind }: {
  read: ReturnType<typeof useSetupProgress<SetupDetail>>; item: SetupDetail | null; renderKind: (kind: string) => ReactNode;
}) {
  const { t } = useTranslation("setup"), { user } = useSession(), { store, storeRefreshing, storeRefreshError } = useSelectedStore();
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null), [outcome, setOutcome] = useState<Outcome | null>(null);
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(false), controller = useRef<AbortController | null>(null), opener = useRef<HTMLButtonElement | null>(null);
  const heading = useRef<HTMLHeadingElement | null>(null), reviewHeading = useRef<HTMLHeadingElement | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (confirmation) reviewHeading.current?.focus(); }, [confirmation]);
  useEffect(() => { if (outcome) heading.current?.focus(); }, [outcome]);
  const manager = canManageStore(user.role), storeCurrent = !storeRefreshing && storeRefreshError === null;
  const fresh = item !== null && read.state.status === "ready" && read.state.data === item && !read.state.refreshing && read.state.refreshError === null && !read.suspended;
  const supported = item !== null && setupRecoverySupported(item);
  const actions = item ? offeredSetupActions(item) : [];
  const allowed = manager && storeCurrent && fresh && supported && !item?.stepRunning && !pending && outcome !== "uncertain";
  const unchanged = confirmation !== null && item !== null && confirmation.fingerprint === JSON.stringify(item) && confirmation.storeName === store.name;
  function closeReview() {
    if (pending) return;
    setConfirmation(null);
    window.requestAnimationFrame(() => {
      if (!active.current) return;
      if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus();
    });
  }
  async function submit() {
    if (!allowed || !unchanged || !confirmation || !item || !read.isCurrent(item) || !actions.includes(confirmation.action)) return;
    const target = confirmation;
    await run(async () => {
      read.suspend(); setOutcome(null);
      const request = new AbortController(); controller.current = request;
      try {
        const reply = await setupProgressApi.act(store.id, target.item.id, target.action, request.signal);
        if (!active.current) return;
        if (reply.kind !== target.item.kind || reply.subject !== target.item.subject || reply.createdAt !== target.item.createdAt) throw new HttpError("invalid-response", 200);
        read.accept(reply); setOutcome("accepted");
      } catch (failure) {
        if (!active.current) return;
        const refused = isHttpError(failure) && failure.kind === "http" && [401, 403, 404, 409, 429].includes(failure.status ?? 0);
        setOutcome(refused ? "refused" : "uncertain"); throw failure;
      } finally {
        controller.current = null;
        if (active.current) { setConfirmation(null); read.resume(); }
      }
    });
  }
  const serverDetail = outcome === "refused" && isHttpError(error) && error.status === 409 && typeof error.problem?.detail === "string" ? error.problem.detail : null;
  return <section className="setup-recovery" aria-labelledby="setup-recovery-title" aria-busy={pending}>
    <h2 id="setup-recovery-title" ref={heading} tabIndex={-1}>{t("recovery.title")}</h2>
    {!manager && <p className="hint">{t("recovery.readOnly")}</p>}
    {manager && item && <>
      {!storeCurrent && <p role="status">{t("recovery.storeStale")}</p>}
      {!fresh && !pending && <p role="status">{t("recovery.stale")}</p>}
      {!supported && <p>{t("recovery.unsupported")}</p>}
      {item.stepRunning && <p>{t("recovery.leased")}</p>}
      {supported && actions.length === 0 && <p className="hint">{t("recovery.noActions")}</p>}
      <div className="cluster">{actions.map(action => <Button key={action} type="button" variant={action === "cancel" ? "secondary" : "primary"}
        disabled={!allowed || confirmation !== null} onClick={event => {
          if (!allowed || !read.isCurrent(item)) return;
          opener.current = event.currentTarget;
          setConfirmation({ action, item: structuredClone(item), fingerprint: JSON.stringify(item), storeName: store.name });
        }}>{t(`recovery.actions.${action}.label`)}</Button>)}</div>
    </>}
    {manager && item && confirmation && <form className="setup-confirmation" aria-labelledby="setup-confirmation-title" aria-busy={pending}
      onSubmit={event => { event.preventDefault(); void submit(); }} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); closeReview(); } }}>
      <h3 id="setup-confirmation-title" tabIndex={-1} ref={reviewHeading}>{t(`recovery.actions.${confirmation.action}.title`)}</h3>
      <dl className="setup-facts">
        <div><dt>{t("recovery.store")}</dt><dd>{confirmation.storeName}</dd></div>
        <div><dt>{t("kindCode")}</dt><dd>{renderKind(confirmation.item.kind)}</dd></div>
        <div><dt>{t("recovery.subject")}</dt><dd>{confirmation.item.subject ?? t("wholeStore")}</dd></div>
        <div><dt>{t("id")}</dt><dd><code>{confirmation.item.id}</code></dd></div>
      </dl>
      <p>{t(`recovery.actions.${confirmation.action}.hint`)}</p>
      {!unchanged && <p role="status">{t("recovery.changed")}</p>}
      <div className="cluster"><Button type="submit" busy={pending} disabled={!allowed || !unchanged}>{t(`recovery.actions.${confirmation.action}.confirm`)}</Button>
        <Button type="button" variant="quiet" disabled={pending} onClick={closeReview}>{t("recovery.closeReview")}</Button></div>
    </form>}
    {pending && <p role="status">{t("recovery.pending")}</p>}
    {outcome && <div className="setup-action-result">
      <p role="status">{t(`recovery.${outcome}`)}</p>
      {outcome !== "accepted" && error !== null && <RequestError error={error} operation="write" />}
      {serverDetail !== null && item && <details><summary>{t("recovery.serverDetail")}</summary><p>{serverDetail}</p></details>}
      {outcome === "uncertain" && manager && <Button type="button" variant="secondary" disabled={!fresh || !storeCurrent || pending} onClick={() => {
        if (item && read.isCurrent(item) && storeCurrent && !pending) { setOutcome(null); heading.current?.focus(); }
      }}>{t("recovery.finishReview")}</Button>}
    </div>}
  </section>;
}
