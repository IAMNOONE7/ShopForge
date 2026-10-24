import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useSelectedStore, type StoreOutletContext } from "../adminContext";
import { api } from "../api";
import { domainNeedsRefresh, type StoreDomain } from "../api/domains";
import { isHttpError } from "../api/http";
import { statusOf } from "../api/errors";
import { domainProblem, normalizedDomain } from "../components/domainValidation";
import { DomainEdgeProgress } from "../components/DomainEdgeProgress";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useSetupProgress } from "../useSetupProgress";
import { formatDateTime } from "../utils/format";
import { ForbiddenPage } from "./RouteStatePage";
import "./StoreDomainsPage.css";

type Operation = "add" | "verify" | "primary" | "remove";
type Confirmation = { kind: "primary" | "remove"; domain: StoreDomain; storeName: string };

export function StoreDomainsPage() {
  const { user } = useSession();
  const context = useSelectedStore();
  if (!canManageStore(user.role)) return <ForbiddenPage />;
  return <DomainsWorkspace key={`${user.tenantId}:${user.id}:${user.role}:${context.store.id}`} {...context} />;
}

function DomainsWorkspace({ store, reloadStores, storeRefreshing, storeRefreshError }: StoreOutletContext) {
  const { t, i18n } = useTranslation("domains");
  const { t: edge } = useTranslation("domainEdge"), { t: setup } = useTranslation("setup");
  const read = useSetupProgress(`domains:${store.id}`, async signal => ({ items: await api.storeDomains(store.id, signal) }), data => data.items.some(domainNeedsRefresh));
  const domains = read.state, reloadDomains = read.refresh;
  const [host, setHost] = useState("");
  const [invalid, setInvalid] = useState(false);
  const [showError, setShowError] = useState(false);
  const [operation, setOperation] = useState<Operation>("add");
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
  const [success, setSuccess] = useState<Operation | null>(null);
  const [uncertain, setUncertain] = useState(false);
  const [now, setNow] = useState(Date.now);
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const heading = useRef<HTMLHeadingElement>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);
  const opener = useRef<HTMLButtonElement | null>(null);
  const mounted = useRef(false);
  const request = useRef<AbortController | null>(null);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; request.current?.abort(); }; }, []);
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);
  useEffect(() => { if (confirmation) confirmButton.current?.focus(); }, [confirmation]);
  const readError = domains.status === "ready" ? domains.refreshError : domains.status === "loading" ? null : domains.error;
  const visible = domains.status === "ready" && ![401, 403, 404].includes(statusOf(readError) ?? 0);
  const data = visible && domains.status === "ready" ? domains.data.items : [];
  const refreshing = domains.status === "loading" || (domains.status === "ready" && domains.refreshing);
  const fresh = domains.status === "ready" && !refreshing && readError === null && !read.suspended;
  const stale = !fresh || storeRefreshing || storeRefreshError !== null;
  const disabled = pending || stale || uncertain;
  const confirmationChanged = confirmation !== null && (confirmation.storeName !== store.name || !data.some(domain => JSON.stringify(domain) === JSON.stringify(confirmation.domain)));
  const knownProblem = showError ? domainProblem(error, operation) : null;
  const fieldError = invalid ? t("hostInvalid") : showError && operation === "add" && isHttpError(error) && error.problem?.errors?.hostName
    ? t(knownProblem === "platformAddress" ? "platformAddress" : "hostInvalid") : undefined;

  function closeConfirmation() {
    if (pending) return;
    setConfirmation(null);
    window.requestAnimationFrame(() => {
      if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus();
      else heading.current?.focus();
    });
  }
  async function change(kind: Operation, domain?: StoreDomain) {
    if (disabled || domains.status !== "ready" || !read.isCurrent(domains.data) || (confirmation && (kind !== confirmation.kind || confirmationChanged))) return;
    if (kind !== "add" && (!domain || !data.some((item) => JSON.stringify(item) === JSON.stringify(domain)))) return;
    if (kind === "primary" && (!domain?.isVerified || domain.isPrimary)) return;
    if (kind === "remove" && domain?.isPrimary) return;
    const normalized = normalizedDomain(host);
    if (kind === "add" && !normalized) { setInvalid(true); document.getElementById("domain-host-name")?.focus(); return; }
    await run(async () => {
      setOperation(kind); setShowError(true); setSuccess(null); read.suspend();
      const controller = new AbortController(); request.current = controller;
      try {
        if (kind === "add") await api.addStoreDomain(store.id, normalized!, controller.signal);
        else if (kind === "verify") await api.verifyStoreDomain(store.id, domain!.id, controller.signal);
        else if (kind === "primary") await api.makeStoreDomainPrimary(store.id, domain!.id, controller.signal);
        else await api.removeStoreDomain(store.id, domain!.id, controller.signal);
        if (!mounted.current) return;
        if (kind === "add") setHost("");
        setConfirmation(null); setSuccess(kind);
        heading.current?.focus();
      } catch (exception) {
        if (!mounted.current) return;
        const refused = isHttpError(exception) && exception.kind === "http" && [400, 401, 403, 404, 409, 429].includes(exception.status ?? 0);
        setUncertain(!refused); setConfirmation(null); heading.current?.focus();
        throw exception;
      } finally {
        request.current = null;
        if (mounted.current) { read.resume(); reloadStores(); }
      }
    });
  }
  function add(event: FormEvent<HTMLFormElement>) { event.preventDefault(); void change("add"); }

  return <div className="domain-workspace">
    <div className="domain-page-heading">
      <div><h1 ref={heading} tabIndex={-1}>{t("title")}</h1><p className="hint">{t("intro")}</p></div>
      <Link to={`/stores/${store.id}/settings`}>{t("backToSettings")}</Link>
    </div>
    <InlineMessage>{t("edgeHint")}</InlineMessage>
    <div className="section-heading">
      <h2>{t("addresses")}</h2>
      <div className="cluster"><Button type="button" variant="secondary" disabled={pending || refreshing || storeRefreshing} onClick={() => { setConfirmation(null); reloadDomains(); reloadStores(); }}>{t("refresh")}</Button>
        {read.canPause && <Button type="button" variant="quiet" onClick={read.toggle}>{setup(read.paused ? "resume" : "pause")}</Button>}</div>
    </div>
    <p className="hint">{setup(`updates.${read.mode}`)}</p>
    {refreshing && <LoadingState label={t("loading")} lines={2} />}
    {pending && <p role="status">{t(operation === "verify" ? "checkingDns" : "saving")}</p>}
    {readError !== null && <RequestError error={readError} operation="read" onRetry={reloadDomains} />}
    {stale && visible && <p className="hint" role="status">{t("stale")}</p>}
    {(storeRefreshing || storeRefreshError !== null) && <div className="domain-metadata-status">
      <p className="hint">{t("metadataStale")}</p>
      <Button type="button" variant="secondary" disabled={pending || storeRefreshing} onClick={reloadStores}>{t("refreshStore")}</Button>
    </div>}
    {success && <InlineMessage tone="success">{t(`success.${success}`)}</InlineMessage>}
    {success === "remove" && <div className="domain-removal-note"><p>{edge("removalPending")}</p><Link to={`/stores/${store.id}/setup`}>{edge("allSetup")}</Link></div>}
    {showError && error !== null && <div>
      {knownProblem && <InlineMessage tone="error">{t(knownProblem)}</InlineMessage>}
      <RequestError error={error} operation="write" fieldErrors={operation === "add" && fieldError ? [{ field: "hostName", message: fieldError }] : []} />
      <p className="hint">{t("failureHint")}</p>
    </div>}
    {uncertain && <div className="domain-removal-note"><p role="status">{edge("uncertain")}</p><Link to={`/stores/${store.id}/setup`}>{edge("allSetup")}</Link>
      <Button type="button" variant="secondary" disabled={!fresh || pending || storeRefreshing || storeRefreshError !== null} onClick={() => {
        if (domains.status === "ready" && read.isCurrent(domains.data) && !storeRefreshing && storeRefreshError === null && !pending) { setUncertain(false); setShowError(false); heading.current?.focus(); }
      }}>{edge("finishReview")}</Button></div>}
    {fresh && data.length === 0 && <p>{t("empty")}</p>}
    <ul className="domain-list">
      {data.map((domain) => <li key={domain.id} className="store-settings-panel domain-card">
        <div className="domain-card-heading">
          <h3>{domain.hostName}</h3>
          <div className="cluster"><span className="badge">{t(domain.isVerified ? "verified" : "unverified")}</span>{domain.isPrimary && <span className="badge">{t("primary")}</span>}</div>
        </div>
        {domain.isPrimary && <p className="hint">{t("primaryHint")}</p>}
        {domain.verifiedAt && <p>{t("verifiedOn", { date: formatDateTime(domain.verifiedAt, i18n.resolvedLanguage) })}</p>}
        {!domain.isVerified && <>
          <p>{t("txtInstructions")}</p>
          <div className="domain-dns-record">
            <div><strong>{t("recordType")}</strong><p>TXT</p></div>
            <DnsValue label={t("recordName")} value={domain.challengeName!} />
            <DnsValue label={t("recordValue")} value={domain.challengeValue!} />
          </div>
          <p className="hint">{t("dnsHint")}</p>
          {domain.claimExpiresAt ? <p className={Date.parse(domain.claimExpiresAt) <= now ? "domain-expired" : "hint"}>
            {t(Date.parse(domain.claimExpiresAt) <= now ? "expired" : "expires", { date: formatDateTime(domain.claimExpiresAt, i18n.resolvedLanguage) })}
          </p> : <p className="hint">{t("expiryUnknown")}</p>}
          <p className="hint">{t("cleanupHint")}</p>
        </>}
        <DomainEdgeProgress domain={domain} storeId={store.id} />
        <div className="cluster domain-actions">
          {!domain.isVerified && <Button type="button" disabled={disabled || confirmation !== null} onClick={() => void change("verify", domain)}>{t("verify", { host: domain.hostName })}</Button>}
          {!domain.isPrimary && <>
            <Button type="button" variant="secondary" disabled={disabled || !domain.isVerified || confirmation !== null}
              onClick={(event) => { if (domains.status !== "ready" || !read.isCurrent(domains.data)) return; opener.current = event.currentTarget; setSuccess(null); setConfirmation({ kind: "primary", domain: structuredClone(domain), storeName: store.name }); }}>{t("makePrimary", { host: domain.hostName })}</Button>
            <Button type="button" variant="quiet" disabled={disabled || confirmation !== null}
              onClick={(event) => { if (domains.status !== "ready" || !read.isCurrent(domains.data)) return; opener.current = event.currentTarget; setSuccess(null); setConfirmation({ kind: "remove", domain: structuredClone(domain), storeName: store.name }); }}>{t("remove", { host: domain.hostName })}</Button>
          </>}
        </div>
        {confirmation?.domain.id === domain.id && <div className="domain-confirmation" role="group" aria-label={t(confirmation.kind === "primary" ? "confirmPrimary" : "confirmRemove", { host: domain.hostName })} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); closeConfirmation(); } }}>
          <h4>{t(confirmation.kind === "primary" ? "confirmPrimary" : "confirmRemove", { host: domain.hostName })}</h4>
          <p>{edge("storeTarget", { store: confirmation.storeName })}</p>
          <p>{t(confirmation.kind === "primary" ? "primaryWarning" : "removeWarning")}</p>
          {confirmationChanged && <p role="status">{t("stale")}</p>}
          <div className="cluster">
            <Button ref={confirmButton} type="button" variant={confirmation.kind === "remove" ? "danger" : "primary"} busy={pending} busyLabel={t("saving")} disabled={disabled || confirmationChanged}
              onClick={() => void change(confirmation.kind, confirmation.domain)}>{t("confirm")}</Button>
            <Button type="button" variant="secondary" disabled={pending} onClick={closeConfirmation}>{t("cancel")}</Button>
          </div>
        </div>}
      </li>)}
    </ul>
    <section className="store-settings-panel" aria-labelledby="domain-add-title">
      <h2 id="domain-add-title">{t("addTitle")}</h2><p className="hint">{t("ownDomainHint")}</p>
      <form noValidate onSubmit={add} className="domain-add-form">
        <Field id="domain-host-name" name="hostName" label={t("hostLabel")} hint={t("hostHint")} value={host} autoCapitalize="none" autoCorrect="off" spellCheck={false}
          disabled={disabled || confirmation !== null} error={fieldError} onChange={(event) => { setHost(event.target.value); setInvalid(false); setShowError(false); setSuccess(null); }} />
        <Button type="submit" busy={pending && operation === "add"} busyLabel={t("saving")} disabled={disabled || confirmation !== null}>{t("add")}</Button>
      </form>
    </section>
    <Link to={`/stores/${store.id}/setup`}>{edge("allSetup")}</Link>
  </div>;
}

function DnsValue({ label, value }: { label: string; value: string }) {
  const { t } = useTranslation("domains");
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");
  async function copy() {
    try { await navigator.clipboard.writeText(value); setState("copied"); }
    catch { setState("failed"); }
  }
  return <div className="domain-dns-value">
    <Field label={label} value={value} readOnly onFocus={(event) => event.currentTarget.select()} />
    <Button type="button" variant="secondary" onClick={() => void copy()}>{t("copy", { label })}</Button>
    {state !== "idle" && <span role="status" className="hint">{t(state === "copied" ? "copied" : "copyFailed")}</span>}
  </div>;
}
