import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { useSelectedStore } from "../adminContext";
import { statusOf } from "../api/errors";
import { isSetupId, setupIsActive, setupProgressApi, type SetupDetail, type SetupSummary } from "../api/setupProgress";
import { Button } from "../components/ui/Button";
import { SetupRecoveryPanel } from "../components/SetupRecoveryPanel";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useSession } from "../session";
import { useSetupProgress } from "../useSetupProgress";
import type { RequestState } from "../useRequest";
import { formatDateTime, formatNumber } from "../utils/format";
import "./StoreSetupPage.css";

const states = ["running", "awaitingInput", "failed", "succeeded", "cancelling", "cancelled"] as const;
const stepStates = ["pending", "waiting", "awaitingInput", "succeeded", "failed", "undone", "kept"] as const;
const kinds = { "email.domain": "kinds.email_domain", "email.domain.remove": "kinds.email_domain_remove", "domain.bind": "kinds.domain_bind",
  "domain.unbind": "kinds.domain_unbind", "domain.revalidate": "kinds.domain_revalidate", "domain.zone": "kinds.domain_zone" } as const;
function Kind({ kind }: { kind: string }) {
  const { t } = useTranslation("setup");
  return <>{Object.hasOwn(kinds, kind) ? t(kinds[kind as keyof typeof kinds]) : t("unknownKind", { kind })}</>;
}
function Status({ value, step = false }: { value: string; step?: boolean }) {
  const { t } = useTranslation("setup");
  const state = states.find(state => state === value), stepState = stepStates.find(state => state === value);
  const label = step ? stepState && t(`stepStates.${stepState}`) : state && t(`states.${state}`);
  const tone = value === "succeeded" ? "success" : value === "failed" ? "error" : "neutral";
  return <span className={`setup-status setup-status-${tone}`}>{label ?? t("unknownState", { state: value })}</span>;
}
function DateFact({ value }: { value: string | null }) {
  const { t, i18n } = useTranslation("setup");
  return value === null ? <>{t("notRecorded")}</> : <time dateTime={value}>{formatDateTime(value, i18n.resolvedLanguage)}</time>;
}
function Feedback({ state, refresh }: { state: RequestState<unknown>; refresh: () => void }) {
  const { t } = useTranslation("setup");
  const error = state.status === "ready" ? state.refreshError : state.status === "loading" ? null : state.error;
  return <>
    {state.status === "loading" && <LoadingState label={t("loading")} lines={3} />}
    {state.status === "ready" && state.refreshing && <p role="status">{t("refreshing")}</p>}
    {error !== null && <RequestError error={error} operation="read" onRetry={refresh} />}
    {state.status === "ready" && ![401, 403, 404].includes(statusOf(error) ?? 0) && (state.refreshing || error !== null) && <p className="setup-stale" role="status">{t("stale")}</p>}
  </>;
}
function RefreshControls({ read }: { read: Pick<ReturnType<typeof useSetupProgress<unknown>>, "refreshing" | "refresh" | "canPause" | "paused" | "toggle" | "mode" | "suspended"> }) {
  const { t } = useTranslation("setup");
  return <div className="setup-refresh">
    <div className="cluster"><Button variant="secondary" disabled={read.refreshing || read.suspended} onClick={read.refresh}>{t("refresh")}</Button>
      {read.canPause && <Button variant="quiet" onClick={read.toggle}>{t(read.paused ? "resume" : "pause")}</Button>}</div>
    <p className="hint">{t(`updates.${read.mode}`)}</p>
  </div>;
}
function visibleRead<T>(state: RequestState<T>) {
  if (state.status !== "ready" || [401, 403, 404].includes(statusOf(state.refreshError) ?? 0)) return null;
  return state;
}
function useScope() {
  const { user } = useSession(), { store } = useSelectedStore();
  return `${user.tenantId}:${user.id}:${user.role}:${store.id}`;
}
export function StoreSetupPage() {
  return <SetupList key={useScope()} />;
}
function SetupList() {
  const { store } = useSelectedStore(), { t, i18n } = useTranslation("setup");
  const read = useSetupProgress(`setup-list:${useScope()}`, signal => setupProgressApi.list(store.id, signal), items => items.some(item => setupIsActive(item.state)));
  const ready = visibleRead(read.state), path = `/stores/${store.id}/setup`;
  return <div className="setup-page">
    <h1>{t("title")}</h1><p className="hint">{t("scope", { store: store.name })}</p>
    <p>{t("window")}</p><RefreshControls read={read} /><Feedback state={read.state} refresh={read.refresh} />
    {ready && <>
      <p className="hint">{t("count", { formatted: formatNumber(ready.data.length, i18n.resolvedLanguage) })}</p>
      {!ready.refreshing && ready.refreshError === null && ready.data.length === 0 && <EmptyState headingLevel={2} title={t("emptyTitle")}>{t("emptyBody")}</EmptyState>}
      <ul className="setup-list">{ready.data.map((item: SetupSummary) => <li key={item.id}>
        <div className="setup-card-heading"><h2><Link to={`${path}/${item.id}`}><Kind kind={item.kind} /></Link></h2><Status value={item.state} /></div>
        <p className="setup-subject">{item.subject ?? t("wholeStore")}</p>
        <dl className="setup-facts">
          <div><dt>{t("id")}</dt><dd><code>{item.id}</code></dd></div>
          <div><dt>{t("currentStep")}</dt><dd>{item.currentStep ?? t("notRecorded")}</dd></div>
          <div><dt>{t("createdAt")}</dt><dd><DateFact value={item.createdAt} /></dd></div>
          <div><dt>{t("finishedAt")}</dt><dd><DateFact value={item.finishedAt} /></dd></div>
        </dl>
      </li>)}</ul>
    </>}
    <p className="hint">{t("messageHint")} <Link to={`/stores/${store.id}/operations`}>{t("messageDelivery")}</Link></p>
  </div>;
}
export function StoreSetupDetailPage() {
  const scope = useScope(), { operationId = "" } = useParams(), { store } = useSelectedStore(), { t } = useTranslation("setup");
  if (!isSetupId(operationId)) return <div className="setup-page"><h1>{t("detailTitle")}</h1><Link to={`/stores/${store.id}/setup`}>{t("back")}</Link><EmptyState headingLevel={2} title={t("invalidTitle")}>{t("invalidBody")}</EmptyState></div>;
  return <SetupDetailPage key={`${scope}:${operationId.toLowerCase()}`} operationId={operationId} />;
}
function SetupDetailPage({ operationId }: { operationId: string }) {
  const { store } = useSelectedStore(), { t } = useTranslation("setup");
  const read = useSetupProgress(`setup-detail:${useScope()}:${operationId.toLowerCase()}`, signal => setupProgressApi.detail(store.id, operationId, signal), item => setupIsActive(item.state));
  const ready = visibleRead(read.state);
  const error = read.state.status === "ready" ? read.state.refreshError : read.state.status === "loading" ? null : read.state.error;
  return <div className="setup-page">
    <Link to={`/stores/${store.id}/setup`}>{t("back")}</Link><h1>{t("detailTitle")}</h1><p className="hint">{t("scope", { store: store.name })}</p>
    <RefreshControls read={read} /><Feedback state={read.state} refresh={read.refresh} />
    {statusOf(error) === 404 && <EmptyState headingLevel={2} title={t("missingTitle")}>{t("missingBody")}</EmptyState>}
    <SetupRecoveryPanel read={read} item={ready?.data ?? null} renderKind={kind => <Kind kind={kind} />} />
    {ready && <Detail item={ready.data} />}
  </div>;
}
function Detail({ item }: { item: SetupDetail }) {
  const { t, i18n } = useTranslation("setup");
  return <>
    <section className="setup-overview" aria-labelledby="setup-kind">
      <div className="setup-card-heading"><h2 id="setup-kind"><Kind kind={item.kind} /></h2><Status value={item.state} /></div>
      <p className="setup-subject">{item.subject ?? t("wholeStore")}</p>
      <dl className="setup-facts">
        <div><dt>{t("id")}</dt><dd><code>{item.id}</code></dd></div>
        <div><dt>{t("kindCode")}</dt><dd><code>{item.kind}</code></dd></div>
        <div><dt>{t("createdAt")}</dt><dd><DateFact value={item.createdAt} /></dd></div>
        <div><dt>{t("changedAt")}</dt><dd><DateFact value={item.changedAt} /></dd></div>
        <div><dt>{t("finishedAt")}</dt><dd><DateFact value={item.finishedAt} /></dd></div>
        <div><dt>{t("nextCheckAt")}</dt><dd><DateFact value={item.nextCheckAt} /></dd></div>
      </dl>
      <p>{t(item.stepRunning ? "stepRunning" : "stepNotRunning")}</p><p className="hint">{t("progressHint")}</p>
    </section>
    <section aria-labelledby="setup-steps"><h2 id="setup-steps">{t("steps")}</h2>
      {item.steps.length === 0 && <p>{t("noSteps")}</p>}
      <ol className="setup-steps">{item.steps.map((step, index) => <li key={`${index}:${step.name}`}>
        <div className="setup-card-heading"><h3>{step.name}</h3><Status value={step.status} step /></div>
        {step.note !== null && <div className="setup-note"><h4>{t("note")}</h4><p>{step.note}</p></div>}
        {step.lastError !== null && <div className="setup-error"><h4>{t("lastError")}</h4><p>{step.lastError}</p><p><DateFact value={step.lastErrorAt} /></p></div>}
        <dl className="setup-facts">
          <div><dt>{t("attempts")}</dt><dd>{formatNumber(step.attempts, i18n.resolvedLanguage)}</dd></div>
          <div><dt>{t("checks")}</dt><dd>{formatNumber(step.checks, i18n.resolvedLanguage)}</dd></div>
          <div><dt>{t("waitingSince")}</dt><dd><DateFact value={step.waitingSince} /></dd></div>
          <div><dt>{t("startedAt")}</dt><dd><DateFact value={step.startedAt} /></dd></div>
          <div><dt>{t("finishedAt")}</dt><dd><DateFact value={step.finishedAt} /></dd></div>
        </dl>
        <details className="setup-technical"><summary>{t("technical")}</summary><dl className="setup-facts">
          <div><dt>{t("resourceId")}</dt><dd>{step.resourceId ?? t("notRecorded")}</dd></div>
          <div><dt>{t("requestId")}</dt><dd>{step.requestId ?? t("notRecorded")}</dd></div>
          <div><dt>{t("createdResource")}</dt><dd>{t(step.created ? "yes" : "no")}</dd></div>
          <div><dt>{t("keepsSecret")}</dt><dd>{t(step.keepsSecret ? "referenceKept" : "noReference")}</dd></div>
        </dl></details>
      </li>)}</ol>
    </section>
  </>;
}
