import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useSelectedStore } from "../adminContext";
import { getStoreDomains } from "../api/domains";
import { statusOf } from "../api/errors";
import { isHttpError } from "../api/http";
import { isSetupId } from "../api/setupProgress";
import { emailStates, storeEmailApi, type ConnectEmailDomain } from "../api/storeEmail";
import { emailDomainDraft, type EmailDraft, type EmailField } from "../components/emailDomainValidation";
import { EmailOperationLink, StoreEmailDetails } from "../components/StoreEmailDetails";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useSetupProgress } from "../useSetupProgress";
import "./StoreEmailPage.css";

type Confirmation = { kind: "connect"; body: ConnectEmailDomain; addresses: string; email: string; store: string }
  | { kind: "remove"; domain: string; senderAddress: string; email: string; store: string };
type Outcome = { kind: "accepted" | "refused" | "uncertain"; operationId: string | null; error: unknown | null };
const fields = ["domain", "senderAddress", "replyTo"] as const;
const errorKeys = { domain: "domainInvalid", senderAddress: "senderInvalid", replyTo: "replyInvalid" } as const;
export function StoreEmailPage() {
  const { user } = useSession(), { store } = useSelectedStore();
  return <EmailWorkspace key={`${user.tenantId}:${user.id}:${user.role}:${store.id}`} />;
}
function EmailWorkspace() {
  const { t } = useTranslation("storeEmail"), { t: setup } = useTranslation("setup"), { user } = useSession();
  const { store, storeRefreshing, storeRefreshError, reloadStores } = useSelectedStore(), manager = canManageStore(user.role);
  const scope = `${user.tenantId}:${user.id}:${user.role}:${store.id}`;
  const read = useSetupProgress(`email:${scope}`, signal => storeEmailApi.get(store.id, signal), item => item.domain !== null && ["connecting", "awaitingDns", "removing"].includes(item.domain.state));
  const addresses = useSetupProgress(`email-addresses:${scope}`, signal => manager ? getStoreDomains(store.id, signal) : Promise.resolve([]), () => false);
  const [draft, setDraft] = useState<EmailDraft>({ domain: "", senderAddress: "", replyTo: "" });
  const [invalid, setInvalid] = useState<ReturnType<typeof emailDomainDraft>["errors"]>({});
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null), [outcome, setOutcome] = useState<Outcome | null>(null);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(false), request = useRef<AbortController | null>(null), opener = useRef<HTMLButtonElement | null>(null);
  const heading = useRef<HTMLHeadingElement | null>(null), confirmHeading = useRef<HTMLHeadingElement | null>(null), result = useRef<HTMLDivElement | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; request.current?.abort(); }; }, []);
  useEffect(() => { if (confirmation) confirmHeading.current?.focus(); }, [confirmation]);
  useEffect(() => { if (outcome) result.current?.focus(); }, [outcome]);
  const readError = read.state.status === "ready" ? read.state.refreshError : read.state.status === "loading" ? null : read.state.error;
  const item = read.state.status === "ready" && ![401, 403, 404].includes(statusOf(readError) ?? 0) ? read.state.data : null;
  const fresh = item !== null && read.state.status === "ready" && !read.state.refreshing && readError === null && !read.suspended;
  const addressError = addresses.state.status === "ready" ? addresses.state.refreshError : addresses.state.status === "loading" ? null : addresses.state.error;
  const addressItems = addresses.state.status === "ready" && ![401, 403, 404].includes(statusOf(addressError) ?? 0) ? addresses.state.data : [];
  const addressesFresh = addresses.state.status === "ready" && !addresses.state.refreshing && addressError === null && !addresses.suspended;
  const storeCurrent = !storeRefreshing && storeRefreshError === null;
  const supported = item !== null && (item.domain === null || emailStates.some(state => state === item.domain?.state) && ["eu", "us"].includes(item.domain.region));
  const canWrite = manager && storeCurrent && fresh && supported && !pending && outcome?.kind !== "uncertain";
  const canConnect = canWrite && item?.domain === null && addressesFresh;
  const canRemove = canWrite && item?.domain != null && item.domain.state !== "removing";
  const unchanged = confirmation !== null && item !== null && confirmation.email === JSON.stringify(item) && confirmation.store === store.name
    && (confirmation.kind !== "connect" || confirmation.addresses === JSON.stringify(addressItems));
  const serverFields = outcome?.kind === "refused" && isHttpError(outcome.error) && outcome.error.status === 400 && outcome.error.problem?.errors;
  const fieldError = (field: EmailField) => invalid[field] ? t(invalid[field]) : serverFields && Object.hasOwn(serverFields, field) ? t(errorKeys[field]) : undefined;
  const fieldErrors = fields.filter(field => fieldError(field)).map(field => ({ field: `email-${field}`, message: fieldError(field)! }));
  const serverDetail = outcome?.kind === "refused" && isHttpError(outcome.error) && outcome.error.status === 409 && typeof outcome.error.problem?.detail === "string" ? outcome.error.problem.detail : null;
  function close() {
    if (pending) return;
    setConfirmation(null);
    window.requestAnimationFrame(() => { if (active.current) { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); } });
  }
  function reviewConnect() {
    if (!canConnect || !item || !read.isCurrent(item) || !addresses.isCurrent(addressItems)) return;
    const checked = emailDomainDraft(draft, addressItems); setInvalid(checked.errors);
    if (!checked.body) { document.getElementById(`email-${fields.find(field => checked.errors[field])}`)?.focus(); return; }
    setOutcome(null); setConfirmation({ kind: "connect", body: checked.body, addresses: JSON.stringify(addressItems), email: JSON.stringify(item), store: store.name });
  }
  async function submit() {
    if (!confirmation || !unchanged || !item || !read.isCurrent(item) || !(confirmation.kind === "connect" ? canConnect && addresses.isCurrent(addressItems) : canRemove)) return;
    const target = confirmation;
    await run(async () => {
      read.suspend(); addresses.suspend(); setOutcome(null); const controller = new AbortController(); request.current = controller;
      try {
        const reply = target.kind === "connect" ? await storeEmailApi.connect(store.id, target.body, controller.signal) : await storeEmailApi.remove(store.id, controller.signal);
        if (active.current) setOutcome({ kind: "accepted", operationId: reply.operationId, error: null });
      } catch (error) {
        if (!active.current) return;
        const refused = isHttpError(error) && error.kind === "http" && [400, 401, 403, 404, 409, 429].includes(error.status ?? 0);
        const operationId = refused && isHttpError(error) && error.status === 409 && typeof error.problem?.operationId === "string" && isSetupId(error.problem.operationId) ? error.problem.operationId : null;
        setOutcome({ kind: refused ? "refused" : "uncertain", operationId, error });
      } finally {
        request.current = null;
        if (active.current) { setConfirmation(null); read.resume(); addresses.resume(); }
      }
    });
  }
  return <div className="email-page">
    <h1 ref={heading} tabIndex={-1}>{t("title")}</h1><p className="hint">{t("scope", { store: store.name })}</p>
    <div className="email-refresh"><div className="cluster"><Button type="button" variant="secondary" disabled={read.refreshing || pending} onClick={read.refresh}>{t("refresh")}</Button>
      {read.canPause && <Button type="button" variant="quiet" onClick={read.toggle}>{setup(read.paused ? "resume" : "pause")}</Button>}</div><p className="hint">{setup(`updates.${read.mode}`)}</p></div>
    {read.state.status === "loading" && <LoadingState label={t("loading")} lines={3} />}
    {readError !== null && <RequestError error={readError} operation="read" onRetry={read.refresh} />}
    {item && !fresh && !pending && <p role="status" className="hint">{t("stale")}</p>}
    {!manager && <p className="hint">{t("readOnly")}</p>}
    {!storeCurrent && <div className="email-panel"><p>{t("storeStale")}</p><Button type="button" variant="secondary" disabled={pending || storeRefreshing} onClick={reloadStores}>{t("refreshStore")}</Button></div>}
    {outcome && <div className="email-panel email-result" ref={result} tabIndex={-1}>
      <h2>{t("resultTitle")}</h2><p role="status">{t(`outcomes.${outcome.kind}`)}</p>
      {outcome.error !== null && <RequestError error={outcome.error} operation="write" fieldErrors={fieldErrors} />}
      {serverDetail !== null && <details><summary>{t("serverDetail")}</summary><p className="email-literal">{serverDetail}</p></details>}
      {outcome.operationId && <EmailOperationLink storeId={store.id} operationId={outcome.operationId} />}
      {outcome.kind === "uncertain" && manager && <Button type="button" variant="secondary" disabled={!fresh || !storeCurrent || pending} onClick={() => {
        if (item && read.isCurrent(item) && storeCurrent && !pending) { setOutcome(null); heading.current?.focus(); }
      }}>{t("finishReview")}</Button>}
    </div>}
    {item && <>
      <StoreEmailDetails item={item} storeId={store.id} />
      {manager && !supported && <p>{t("unsupported")}</p>}
      {manager && item.domain === null && <section className="email-panel" aria-labelledby="email-connect-title">
        <h2 id="email-connect-title">{t("connectTitle")}</h2><p>{t("connectHint")}</p><p className="hint">{t("proofHint")}</p>
        <Link to={`/stores/${store.id}/domains`}>{t("manageDomains")}</Link>
        {addresses.refreshing && <p role="status">{t("addressesLoading")}</p>}
        {addressError !== null && <RequestError error={addressError} operation="read" onRetry={addresses.refresh} />}
        <p>{t("addressesTitle")}</p><ul>{addressItems.filter(address => address.isVerified).map(address => <li key={address.id}>{address.hostName}</li>)}</ul>
        {addressesFresh && !addressItems.some(address => address.isVerified) && <p>{t("noAddresses")}</p>}
        <Button type="button" variant="quiet" disabled={pending || addresses.refreshing} onClick={addresses.refresh}>{t("refreshAddresses")}</Button>
        <form noValidate aria-labelledby="email-connect-title" onSubmit={event => { event.preventDefault(); reviewConnect(); }}>
          <div className="email-form-fields">{fields.map(field => <Field key={field} id={`email-${field}`} name={field} label={t(field === "domain" ? "domainLabel" : field)} hint={t(`fieldHints.${field}`)}
            value={draft[field]} error={fieldError(field)} type={field === "domain" ? "text" : "email"} autoComplete="off" autoCapitalize="none" autoCorrect="off" spellCheck={false}
            disabled={!canConnect || confirmation !== null} onChange={event => { setDraft(current => ({ ...current, [field]: event.target.value })); setInvalid({}); setOutcome(null); }} />)}</div>
          <Button type="submit" disabled={!canConnect || confirmation !== null} onClick={event => { opener.current = event.currentTarget; }}>{t("reviewConnect")}</Button>
        </form>
      </section>}
      {manager && item.domain !== null && <section className="email-panel"><h2>{t("removeTitle")}</h2><p>{t("removeHint")}</p>
        <Button type="button" variant="danger" disabled={!canRemove || confirmation !== null} onClick={event => {
          if (!canRemove || !read.isCurrent(item) || !item.domain) return;
          opener.current = event.currentTarget; setOutcome(null); setConfirmation({ kind: "remove", domain: item.domain.domain, senderAddress: item.domain.senderAddress, email: JSON.stringify(item), store: store.name });
        }}>{t("reviewRemove")}</Button>
      </section>}
    </>}
    {manager && item && confirmation && <form className="email-panel email-confirmation" aria-labelledby="email-confirm-title" aria-busy={pending}
      onSubmit={event => { event.preventDefault(); void submit(); }} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); close(); } }}>
      <h2 id="email-confirm-title" tabIndex={-1} ref={confirmHeading}>{t(confirmation.kind === "connect" ? "confirmConnectTitle" : "confirmRemoveTitle")}</h2>
      <dl className="email-facts"><div><dt>{t("storeLabel")}</dt><dd>{confirmation.store}</dd></div>
        {confirmation.kind === "connect" ? fields.map(field => <div key={field}><dt>{t(field === "domain" ? "domainLabel" : field)}</dt><dd>{confirmation.body[field] ?? t("notProvided")}</dd></div>) : <>
          <div><dt>{t("domainLabel")}</dt><dd>{confirmation.domain}</dd></div><div><dt>{t("senderAddress")}</dt><dd>{confirmation.senderAddress}</dd></div>
        </>}
      </dl><p>{t(confirmation.kind === "connect" ? "connectWarning" : "removeWarning")}</p>
      {!unchanged && <p role="status">{t("changed")}</p>}
      <div className="cluster"><Button type="submit" variant={confirmation.kind === "remove" ? "danger" : "primary"} busy={pending}
        disabled={!unchanged || !(confirmation.kind === "connect" ? canConnect : canRemove)}>{t(confirmation.kind === "connect" ? "confirmConnect" : "confirmRemove")}</Button>
        <Button type="button" variant="quiet" disabled={pending} onClick={close}>{t("closeReview")}</Button></div>
    </form>}
    {pending && <p role="status">{t("pending")}</p>}
    <Link to={`/stores/${store.id}/setup`}>{t("allSetup")}</Link>
  </div>;
}
