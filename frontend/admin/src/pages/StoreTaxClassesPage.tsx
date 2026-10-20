import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSelectedStore, type StoreOutletContext } from "../adminContext";
import { api } from "../api";
import { isHttpError } from "../api/http";
import { taxCountry, type TaxClass } from "../api/taxClasses";
import { TaxClassEditor, type TaxChange, type TaxEditor } from "../components/TaxClassEditor";
import { Button } from "../components/ui/Button";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { formatNumber } from "../utils/format";
import { ForbiddenPage } from "./RouteStatePage";

export function StoreTaxClassesPage() {
  const { user } = useSession(); const context = useSelectedStore();
  if (!canManageCatalog(user.role)) return <ForbiddenPage />;
  const scope = `${user.tenantId}:${user.id}:${user.role}:${context.store.id}`;
  return <TaxClassesWorkspace key={scope} scope={scope} canManage={canManageStore(user.role)} {...context} />;
}

function TaxClassesWorkspace({ scope, canManage, store, storeRefreshing, storeRefreshError }: StoreOutletContext & { scope: string; canManage: boolean }) {
  const { t, i18n } = useTranslation(["tax", "auth"]);
  const [state, reload] = useRequest(`tax-classes:${scope}`, async signal => ({ items: await api.taxClasses(store.id, signal) }));
  const [editor, setEditor] = useState<TaxEditor | null>(null);
  const [success, setSuccess] = useState<TaxChange | null>(null), [attempted, setAttempted] = useState(false);
  const [invalidated, setInvalidated] = useState<{ items: TaxClass[] } | null>(null);
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const heading = useRef<HTMLHeadingElement>(null), opener = useRef<HTMLButtonElement | null>(null), active = useRef(false);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const ready = state.status === "ready" ? state : null, items = ready?.data.items ?? [];
  const refreshing = state.status === "loading" || ready?.refreshing === true;
  const needsRead = !ready || ready.refreshing || ready.refreshError !== null || ready.data === invalidated;
  const storeStale = storeRefreshing || storeRefreshError !== null;
  const changed = editor !== null && "item" in editor && !items.some(item => JSON.stringify(item) === JSON.stringify(editor.item));
  const disabled = !canManage || pending || needsRead || storeStale || changed;
  const readError = ready?.refreshError ?? (state.status === "error" || state.status === "not-found" ? state.error : null);
  const home = taxCountry(store.company?.country ?? "");
  const failed = attempted && error !== null;
  const conflict = isHttpError(error) && error.status === 409;
  const fieldErrors = failed && isHttpError(error) ? (["name", "code", "country", "rate"] as const).filter(field => error.problem?.errors?.[field])
    .map(field => ({ field: `tax-${field}`, message: t(`tax:validation.${field}Invalid`) })) : [];
  function open(next: TaxEditor, button: HTMLButtonElement) {
    if (disabled || editor) return;
    opener.current = button; setSuccess(null); setAttempted(false); setEditor(next);
  }
  function close() {
    if (pending) return;
    setEditor(null); setAttempted(false);
    window.requestAnimationFrame(() => {
      if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus();
      else heading.current?.focus();
    });
  }
  async function save(change: TaxChange) {
    if (disabled || !editor || !ready || change.kind !== editor.kind) return;
    setSuccess(null); setAttempted(true);
    await run(async () => {
      try {
        if (change.kind === "create") await api.createTaxClass(store.id, change.code, change.name);
        else if (change.kind === "rename") await api.renameTaxClass(store.id, change.code, change.name);
        else if (change.kind === "deleteClass") await api.deleteTaxClass(store.id, change.code);
        else if (change.kind === "removeRate") await api.removeTaxRate(store.id, change.code, change.country);
        else await api.setTaxRate(store.id, change.code, change.country, change.rate!);
        if (active.current) { setEditor(null); setSuccess(change); heading.current?.focus(); }
      } finally {
        if (active.current) { setInvalidated(ready.data); reload(); }
      }
    });
  }
  return <div className="tax-page">
    <div className="section-heading"><h1 ref={heading} tabIndex={-1}>{t("tax:title")}</h1>
      <Button type="button" variant="secondary" disabled={refreshing || pending} onClick={reload}>{t("tax:refresh")}</Button></div>
    <p className="hint">{t("tax:scope", { store: store.name })}</p>
    <details className="tax-guidance"><summary>{t("tax:howTitle")}</summary>
      <p>{t("tax:howRates")}</p><p>{t("tax:zeroHint")}</p><p>{t("tax:grossHint")}</p></details>
    <p className="hint">{home ? t("tax:homeCountry", { country: home }) : t("tax:unknownHome")}</p>
    {!canManage && <InlineMessage>{t("auth:readOnly")}</InlineMessage>}
    {storeStale && <InlineMessage>{t("tax:storeStale")}</InlineMessage>}
    {refreshing && <LoadingState label={t(ready ? "tax:refreshing" : "tax:loading")} lines={3} />}
    {readError !== null && <RequestError error={readError} operation="read" onRetry={reload} />}
    {ready && needsRead && <InlineMessage>{t("tax:staleRead")}</InlineMessage>}
    {success && <InlineMessage tone="success">{t(`tax:success.${success.kind}`, { code: success.code, country: success.country })}</InlineMessage>}
    {canManage && <Button type="button" disabled={disabled || editor !== null} onClick={event => open({ kind: "create" }, event.currentTarget)}>{t("tax:create")}</Button>}
    {editor && <>
      {changed && <InlineMessage>{t("tax:changed")}</InlineMessage>}
      <TaxClassEditor editor={editor} items={items} disabled={disabled} pending={pending} onSave={save} onCancel={close} />
      {failed && <>
        <RequestError error={error} operation="write" fieldErrors={fieldErrors} />
        <InlineMessage>{t(conflict && editor.kind === "create" ? "tax:duplicate" : conflict && editor.kind === "deleteClass" ? "tax:inUse" : "tax:writeUnconfirmed")}</InlineMessage>
        {isHttpError(error) && error.problem?.detail && <details className="tax-server-detail"><summary>{t("tax:serverDetail")}</summary><p>{error.problem.detail}</p></details>}
      </>}
    </>}
    {ready && !needsRead && items.length === 0 && <InlineMessage>{t("tax:empty")}</InlineMessage>}
    {ready && <ul className="tax-classes">{items.map(item => <li key={item.code}>
      <div><h2 lang={store.culture}>{item.name}</h2><p><code>{item.code}</code></p></div>
      {canManage && <div className="cluster">
        <Button type="button" variant="secondary" aria-label={t("tax:renameNamed", { code: item.code })} disabled={disabled || editor !== null} onClick={event => open({ kind: "rename", item }, event.currentTarget)}>{t("tax:rename")}</Button>
        <Button type="button" variant="secondary" aria-label={t("tax:addRateNamed", { code: item.code })} disabled={disabled || editor !== null} onClick={event => open({ kind: "addRate", item }, event.currentTarget)}>{t("tax:addRate")}</Button>
        <Button type="button" variant="quiet" aria-label={t("tax:deleteNamed", { code: item.code })} disabled={disabled || editor !== null} onClick={event => open({ kind: "deleteClass", item }, event.currentTarget)}>{t("tax:deleteClass")}</Button>
      </div>}
      {item.rates.length === 0 ? <p className="hint">{t("tax:noRates")}</p> : <ul className="tax-rates">{item.rates.map(rate => <li key={rate.country}>
        <p><strong>{rate.country}</strong> · {formatNumber(rate.rate, i18n.resolvedLanguage)} %</p>
        {canManage && <div className="cluster">
          <Button type="button" variant="secondary" aria-label={t("tax:editRateNamed", { code: item.code, country: rate.country })} disabled={disabled || editor !== null} onClick={event => open({ kind: "editRate", item, rate }, event.currentTarget)}>{t("tax:editRate")}</Button>
          <Button type="button" variant="quiet" aria-label={t("tax:removeRateNamed", { code: item.code, country: rate.country })} disabled={disabled || editor !== null} onClick={event => open({ kind: "removeRate", item, rate }, event.currentTarget)}>{t("tax:removeRate")}</Button>
        </div>}
      </li>)}</ul>}
    </li>)}</ul>}
  </div>;
}
