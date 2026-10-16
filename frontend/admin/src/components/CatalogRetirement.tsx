import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation, useNavigate } from "react-router";
import { api } from "../api";
import type { CatalogAction, CatalogKind } from "../api/catalogLifecycle";
import { isHttpError } from "../api/http";
import type { ActionRunner } from "../useAction";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

const holdingReasons = {
  "it has been ordered": "ordered",
  "an address it used to answer to still points at it": "history",
  "another category sits beneath it": "children",
  "listings are in it": "listings",
  "a shopping engine's own name has been mapped to it": "feed",
} as const;

export function CatalogCollections({ archived, activeUrl, archivedUrl }: { archived: boolean; activeUrl: string; archivedUrl: string }) {
  const { t } = useTranslation("retirement");
  return <nav className="catalog-collections" aria-label={t("collections")}>
    <Link to={activeUrl} aria-current={!archived ? "page" : undefined}>{t("active")}</Link>
    <Link to={archivedUrl} aria-current={archived ? "page" : undefined}>{t("archived")}</Link>
  </nav>;
}

export function CatalogChangeNotice({ storeId, kind }: { storeId: string; kind: CatalogKind }) {
  const { t } = useTranslation("retirement");
  const location = useLocation();
  const change = location.state?.catalogChange;
  if (!change || change.storeId !== storeId || change.kind !== kind || typeof change.name !== "string"
    || !["archive", "restore", "delete"].includes(change.action)) return null;
  return <InlineMessage tone="success">{t(`success.${change.action as CatalogAction}`, { name: change.name })}</InlineMessage>;
}

export function CatalogRetirement({ storeId, kind, id, name, snapshot, archived, allowed, disabled, pending, run, reload, onOpenChange }: {
  storeId: string; kind: CatalogKind; id: string; name: string; snapshot: string; archived: boolean; allowed: boolean;
  disabled: boolean; pending: boolean; run: ActionRunner; reload: () => void; onOpenChange: (open: boolean) => void;
}) {
  const { t } = useTranslation("retirement");
  const navigate = useNavigate();
  const [confirmation, setConfirmation] = useState<{ action: CatalogAction; snapshot: string } | null>(null);
  const [verified, setVerified] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const active = useRef(true);
  const title = useRef<HTMLHeadingElement>(null);
  const feedback = useRef<HTMLDivElement>(null);
  const opener = useRef<HTMLButtonElement | null>(null);
  const stale = confirmation !== null && confirmation.snapshot !== snapshot;
  const inUse = confirmation?.action === "delete" && isHttpError(error) && error.status === 409;
  const reason = inUse && isHttpError(error) ? Object.entries(holdingReasons).find(([text]) =>
    error.problem?.detail === `It cannot be deleted because ${text}. Archive it instead to take it out of use and keep it.`)?.[1] : undefined;

  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => { if (confirmation) title.current?.focus(); }, [confirmation]);
  useEffect(() => { if (error !== null) feedback.current?.focus(); }, [error]);

  function open(action: CatalogAction, button: HTMLButtonElement) {
    if (disabled || pending || !allowed) return;
    if (!confirmation) opener.current = button;
    setError(null); setVerified(false); setConfirmation({ action, snapshot }); onOpenChange(true);
  }
  function close() {
    if (pending) return;
    setConfirmation(null); setError(null); onOpenChange(false);
    window.requestAnimationFrame(() => opener.current?.focus());
  }
  async function submit() {
    if (!confirmation || disabled || pending || stale || !allowed || (confirmation.action === "delete" && !verified)) return;
    const action = confirmation.action;
    setError(null);
    await run(async () => {
      try {
        await api.changeCatalogLifecycle(storeId, kind, id, action);
        if (active.current) {
          await navigate(`/stores/${storeId}/${kind}${action === "archive" || (action === "delete" && archived) ? "?archived=true" : ""}`, {
            state: { catalogChange: { storeId, kind, action, name } },
          });
        }
      } catch (failure) {
        if (active.current) { setError(failure); setVerified(false); }
        throw failure;
      } finally {
        if (active.current) reload();
      }
    });
  }

  return <section className="physical-panel catalog-retirement" aria-labelledby="catalog-retirement-title">
    <h2 id="catalog-retirement-title">{t("title")}</h2>
    <p>{t(archived ? "archivedHint" : kind === "products" ? "listingHint" : "categoryHint")}</p>
    {archived && <p className="hint">{t("restoreHint")}</p>}
    {allowed && <div className="catalog-action-row" hidden={confirmation !== null}>
      <Button type="button" variant="secondary" disabled={disabled || pending} onClick={(event) => open(archived ? "restore" : "archive", event.currentTarget)}>{t(`actions.${archived ? "restore" : "archive"}`)}</Button>
      <Button type="button" variant="danger" disabled={disabled || pending} onClick={(event) => open("delete", event.currentTarget)}>{t("actions.delete")}</Button>
    </div>}
    {confirmation && <form className="catalog-confirmation" aria-labelledby="catalog-confirmation-title" onSubmit={(event) => { event.preventDefault(); void submit(); }}
      onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); close(); } }}>
      <h3 id="catalog-confirmation-title" ref={title} tabIndex={-1}>{t(`confirm.${confirmation.action}`, { name })}</h3>
      <p>{t(confirmation.action === "archive" ? kind === "products" ? "archiveListing" : "archiveCategory" : confirmation.action === "restore" ? "restoreHint" : "deleteHint")}</p>
      {stale && <InlineMessage>{t("changed")}</InlineMessage>}
      {error !== null && <div className="catalog-retirement-feedback" ref={feedback} tabIndex={-1}>
        <RequestError error={error} operation="write" />
        {inUse ? <InlineMessage>{reason ? t(`reasons.${reason}`) : t("inUse")} {t(archived ? "keepArchived" : "offerArchive")}</InlineMessage>
          : <p className="hint">{t("checkAfterError")}</p>}
        {inUse && !archived && <Button type="button" variant="secondary" disabled={disabled || pending || stale} onClick={(event) => open("archive", event.currentTarget)}>{t("actions.archive")}</Button>}
      </div>}
      {confirmation.action === "delete" && <label className="catalog-delete-verification"><input type="checkbox" checked={verified} disabled={disabled || pending || stale}
        onChange={(event) => setVerified(event.target.checked)} /><span>{t("verifyDelete", { name })}</span></label>}
      <div className="catalog-action-row"><Button type="submit" variant={confirmation.action === "delete" ? "danger" : "primary"} busy={pending} busyLabel={t("working")}
        disabled={disabled || stale || (confirmation.action === "delete" && !verified)}>{t(`actions.${confirmation.action}`)}</Button>
        <Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancel")}</Button></div>
    </form>}
  </section>;
}
