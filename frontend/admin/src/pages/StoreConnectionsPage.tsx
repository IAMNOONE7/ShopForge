import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useSelectedStore, type StoreOutletContext } from "../adminContext";
import { api } from "../api";
import { connectionFailure, type ProviderConnection } from "../api/providerConnections";
import { ProviderConnectionEditor, type ConnectionChange, type ConnectionEditor } from "../components/ProviderConnectionEditor";
import { StoreReadinessList } from "../components/StoreReadinessList";
import { Button } from "../components/ui/Button";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { formatDateTime } from "../utils/format";

type Outcome = { kind: "acknowledged" | "refused" | "uncertain"; action: ConnectionChange["kind"]; provider: string; error: ReturnType<typeof connectionFailure> | null };
const same = (a: ProviderConnection | null, b: ProviderConnection | null) => JSON.stringify(a) === JSON.stringify(b);
const configuredEditor = (provider: string) => provider === "comgate" || provider === "packeta";

export function StoreConnectionsPage() {
  const { user } = useSession(); const context = useSelectedStore();
  const scope = `${user.tenantId}:${user.id}:${user.role}:${context.store.id}`;
  return <ConnectionsWorkspace key={scope} scope={scope} allowed={canManageStore(user.role)} {...context} />;
}
function ConnectionsWorkspace({ scope, allowed, store, storeRefreshing, storeRefreshError }: StoreOutletContext & { scope: string; allowed: boolean }) {
  const { t, i18n } = useTranslation(["connections", "auth", "stores"]);
  const [state, reload] = useRequest(`connections:${scope}`, async signal => ({ items: await api.providerConnections(store.id, signal) }));
  const [choices, reloadChoices] = useRequest(`connection-choices:${scope}`, signal => api.providerChoices(store.id, signal));
  const [readiness, reloadReadiness] = useRequest(`connection-readiness:${scope}`, signal => api.storeReadiness(store.id, signal));
  const [editor, setEditor] = useState<ConnectionEditor | null>(null), [outcome, setOutcome] = useState<Outcome | null>(null);
  const [invalidated, setInvalidated] = useState<object | null>(null);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(true), heading = useRef<HTMLHeadingElement>(null), result = useRef<HTMLDivElement>(null), opener = useRef<HTMLButtonElement | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => { if (outcome) result.current?.focus(); }, [outcome]);
  const ready = state.status === "ready" && !state.refreshing && state.refreshError === null && state.data !== invalidated;
  const items = state.status === "ready" ? state.data.items : [];
  const choicesReady = choices.status === "ready" && !choices.refreshing && choices.refreshError === null;
  const offered = choices.status === "ready" ? [...new Set([...choices.data.payment, ...choices.data.shipping])] : [];
  const providers = [...new Set([...offered.filter(provider => provider !== "manual"), ...items.map(item => item.provider)])].sort();
  const stale = editor !== null && !same(editor.item, items.find(item => item.provider === editor.provider) ?? null);
  const storeStale = storeRefreshing || storeRefreshError !== null;
  const disabled = !allowed || pending || !ready || storeStale || outcome?.kind === "uncertain";
  const editorDisabled = disabled || stale || editor?.item === null && (!choicesReady || !offered.includes(editor.provider));
  function open(next: ConnectionEditor, button: HTMLButtonElement) {
    if (disabled || editor || !next.item && (!configuredEditor(next.provider) || !choicesReady || !offered.includes(next.provider))) return;
    opener.current = button; setOutcome(null); setEditor(next);
  }
  function close() {
    if (pending) return; setEditor(null); setOutcome(null);
    window.requestAnimationFrame(() => { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); });
  }
  async function save(change: ConnectionChange) {
    if (editorDisabled || !editor || editor.kind !== change.kind || state.status !== "ready") return;
    const target = editor;
    await run(async () => {
      setOutcome(null); setInvalidated(state.data);
      try {
        if (change.kind === "metadata") await api.saveProviderConnection(store.id, target.provider, change.input);
        else if (change.kind === "secret") await api.setProviderSecret(store.id, target.provider, change.secret);
        else await api.disconnectProvider(store.id, target.provider);
        if (active.current) { setEditor(null); setOutcome({ kind: "acknowledged", action: change.kind, provider: target.provider, error: null }); }
      } catch (failure) {
        if (!active.current) return;
        const error = connectionFailure(failure);
        const refused = error.kind === "http" && error.status !== null && [400, 401, 403, 404, 405, 415, 422, 429].includes(error.status)
          || change.kind === "secret" && error.kind === "http" && error.status === 409;
        if (change.kind !== "metadata" || !refused) setEditor(null);
        setOutcome({ kind: refused ? "refused" : "uncertain", action: change.kind, provider: target.provider, error });
      } finally {
        if (active.current) { reload(); reloadChoices(); reloadReadiness(); }
      }
    });
  }
  function refresh() { if (!pending) { reload(); reloadChoices(); reloadReadiness(); } }
  const readError = state.status === "error" || state.status === "not-found" ? state.error : state.status === "ready" ? state.refreshError : null;
  const choiceError = choices.status === "error" || choices.status === "not-found" ? choices.error : choices.status === "ready" ? choices.refreshError : null;
  const readinessError = readiness.status === "error" || readiness.status === "not-found" ? readiness.error : readiness.status === "ready" ? readiness.refreshError : null;
  const checksStale = readiness.status !== "ready" || readiness.refreshing || readiness.refreshError !== null;
  const fieldErrors = outcome?.error && editor?.kind === "metadata" ? ["merchantId", "environment", "publishableKey", "provider"].filter(field => outcome.error?.problem?.errors?.[field])
    .map(field => ({ field: field === "provider" ? "connection-editor-title" : `connection-${field}`, message: t("connections:serverField") })) : [];
  return <div className="connections-page">
    <div className="section-heading"><h1 ref={heading} tabIndex={-1}>{t("connections:title")}</h1><Button type="button" variant="secondary" disabled={pending || state.status === "loading" || state.status === "ready" && state.refreshing} onClick={refresh}>{t("connections:refresh")}</Button></div>
    <p className="hint">{t("connections:scope", { store: store.name })}</p>
    <details className="connection-guidance"><summary>{t("connections:howTitle")}</summary><p>{t("connections:separateSaves")}</p><p>{t("connections:referenceHint")}</p><p>{t("connections:rotation")}</p></details>
    {!allowed && <InlineMessage>{t("auth:readOnly")}</InlineMessage>}
    {storeStale && <InlineMessage>{t("connections:storeStale")}</InlineMessage>}
    {(state.status === "loading" || state.status === "ready" && state.refreshing) && <LoadingState label={t("connections:loading")} lines={2} />}
    {readError !== null && <RequestError error={connectionFailure(readError)} operation="read" onRetry={reload} />}
    {state.status === "ready" && !ready && <InlineMessage>{t("connections:staleRead")}</InlineMessage>}
    {choiceError !== null && <div className="connection-choice-error"><RequestError error={connectionFailure(choiceError)} operation="read" onRetry={reloadChoices} /><p>{t("connections:discoveryFailed")}</p></div>}
    {outcome && <div className="connection-outcome" ref={result} tabIndex={-1}>
      {outcome.error && <RequestError error={outcome.error} operation="write" fieldErrors={fieldErrors} />}
      <InlineMessage tone={outcome.kind === "acknowledged" ? "success" : outcome.kind === "uncertain" ? "error" : "info"}>{t(outcome.kind === "acknowledged" ? `connections:success.${outcome.action}` : outcome.kind === "uncertain" ? "connections:uncertain" : "connections:refused", { provider: outcome.provider })}</InlineMessage>
      {outcome.action === "secret" && outcome.error?.status === 409 && <p>{t("connections:noSecretStore")}</p>}
      {outcome.action === "secret" && outcome.error?.status === 503 && <p>{t("connections:secretStoreUnavailable")}</p>}
      {outcome.kind === "uncertain" && <Button type="button" variant="secondary" disabled={!ready || pending} onClick={() => { if (ready && !pending) { setOutcome(null); heading.current?.focus(); } }}>{t("connections:finishReview")}</Button>}
    </div>}
    {editor && <>{stale && <InlineMessage>{t("connections:draftStale")}</InlineMessage>}<ProviderConnectionEditor editor={editor} disabled={editorDisabled} pending={pending} onSave={save} onCancel={close} /></>}
    {ready && items.length === 0 && <p>{t("connections:noConnections")}</p>}
    <ul className="connection-list">{providers.map(provider => {
      const item = items.find(item => item.provider === provider) ?? null;
      return <li key={provider} className="connection-card"><div className="section-heading"><h2>{provider === "comgate" ? "Comgate" : provider === "packeta" ? "Packeta" : provider}</h2>{item && <span className="chip">{t(item.isActive ? "connections:active" : "connections:inactive")}</span>}</div>
        <p className="hint">{t(!choicesReady ? "connections:discoveryUnknown" : offered.includes(provider) ? "connections:offered" : "connections:unoffered")}</p>
        {item ? <><dl className="connection-facts"><div><dt>{t("connections:accountReference")}</dt><dd>{item.merchantId}</dd></div>
          <div><dt>{t("connections:environment")}</dt><dd>{item.environment === "Test" || item.environment === "Live" ? t(item.environment === "Test" ? "connections:test" : "connections:live") : t("connections:unknownEnvironment", { value: item.environment })}</dd></div>
          <div><dt>{t("connections:credentialState")}</dt><dd>{t(item.hasSecret ? "connections:hasReference" : "connections:noReference")}</dd></div>
          <div><dt>{t(provider === "packeta" ? "connections:mapKey" : "connections:publicKey")}</dt><dd>{item.publishableKey ?? t("connections:notSet")}</dd></div>
          <div><dt>{t("connections:changedAt")}</dt><dd><time dateTime={item.changedAt}>{formatDateTime(item.changedAt, i18n.resolvedLanguage)}</time></dd></div></dl>
          <p className="hint">{t("connections:referenceHint")}</p></> : <p>{t(configuredEditor(provider) ? "connections:notConfigured" : "connections:noSetup")}</p>}
        {allowed && <div className="cluster">
          {(item || configuredEditor(provider)) && <Button type="button" variant="secondary" disabled={disabled || editor !== null || !item && !choicesReady} aria-label={t("connections:editNamed", { provider })} onClick={event => open({ kind: "metadata", provider, item }, event.currentTarget)}>{t(item ? "connections:edit" : "connections:connect")}</Button>}
          {item && <><Button type="button" variant="secondary" disabled={disabled || editor !== null} aria-label={t("connections:secretNamed", { provider })} onClick={event => open({ kind: "secret", provider, item }, event.currentTarget)}>{t(item.hasSecret ? "connections:replaceSecret" : "connections:addSecret")}</Button>
            <Button type="button" variant="quiet" disabled={disabled || editor !== null} aria-label={t("connections:disconnectNamed", { provider })} onClick={event => open({ kind: "disconnect", provider, item }, event.currentTarget)}>{t("connections:disconnect")}</Button></>}
        </div>}
      </li>;
    })}</ul>
    <section className="connection-readiness" aria-labelledby="connection-readiness-title"><div className="section-heading"><h2 id="connection-readiness-title">{t("connections:readinessTitle")}</h2><Button type="button" variant="secondary" disabled={pending || readiness.status === "loading" || readiness.status === "ready" && readiness.refreshing} onClick={reloadReadiness}>{t("connections:checkReadiness")}</Button></div>
      <p className="hint">{t("connections:readinessHint")}</p>
      {checksStale && <InlineMessage>{t("connections:readinessStale")}</InlineMessage>}
      {readinessError !== null && <RequestError error={connectionFailure(readinessError)} operation="read" onRetry={reloadReadiness} />}
      {readiness.status === "ready" && <StoreReadinessList storeId={store.id} checks={readiness.data.checks.filter(check => ["orders.payment-connected", "orders.shipping-connected"].includes(check.check))} stale={checksStale} />}
      <div className="cluster"><Link to={`/stores/${store.id}/methods`}>{t("connections:methods")}</Link><Link to={`/stores/${store.id}/settings`}>{t("connections:publication")}</Link></div>
    </section>
  </div>;
}
