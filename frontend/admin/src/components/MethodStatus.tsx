import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { fieldIssues } from "../api/errors";
import { isHttpError } from "../api/http";
import type { ProviderConnection } from "../api/providerConnections";
import type { MethodRecord, useMethodEditor } from "../useMethodEditor";
import type { RequestState } from "../useRequest";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

export function MethodStatus<T extends MethodRecord>({ work, kind }: { work: ReturnType<typeof useMethodEditor<T>>; kind: "payment" | "shipping" }) {
  const { t } = useTranslation("methodSetup");
  const { state, providers, outcome, resultRef } = work;
  const readError = state.status === "error" || state.status === "not-found" ? state.error : state.status === "ready" ? state.refreshError : null;
  const providerError = providers.status === "error" || providers.status === "not-found" ? providers.error : providers.status === "ready" ? providers.refreshError : null;
  const known = ["name", "providerKey", "price", "vatRate", "taxClass", "maxWeightGrams", "countries", "requiresPickupPoint", "isActive"];
  const errors = fieldIssues(outcome?.error).flatMap(issue => {
    const field = known.find(field => field.toLowerCase() === issue.field.toLowerCase());
    return field && work.editor ? [{ field: kind === "shipping" ? `shipping-${work.editor.item?.code ?? "new"}-${field}` : `payment-${field}`, message: t("serverField") }] : [];
  });
  return <>
    {state.status === "loading" && <LoadingState label={t("loading")} lines={2} />}
    {readError !== null && <RequestError error={readError} operation="read" onRetry={work.reload} />}
    {providerError !== null && <RequestError error={providerError} operation="read" onRetry={work.reloadProviders} />}
    {!work.ready && state.status === "ready" && <InlineMessage>{t("readRequired")}</InlineMessage>}
    {work.stale && <InlineMessage>{t("draftStale")}</InlineMessage>}
    {outcome && <div className="method-result shipping-method-result" ref={resultRef} tabIndex={-1}>
      {outcome.error !== null && <RequestError error={outcome.error} operation="write" fieldErrors={errors} />}
      <InlineMessage tone={outcome.kind === "acknowledged" ? "success" : outcome.kind === "uncertain" ? "error" : "info"}>{t(outcome.kind)}</InlineMessage>
      {isHttpError(outcome.error) && outcome.error.status === 409 && <p>{t("duplicate")}</p>}
      {outcome.kind === "uncertain" && <Button type="button" variant="secondary" disabled={!work.ready || work.pending} onClick={work.finishReview}>{t("finishReview")}</Button>}
    </div>}
  </>;
}

export function MethodProviderNotice({ storeId, provider, offered, connections }: {
  storeId: string; provider: string; offered: boolean | null; connections?: RequestState<ProviderConnection[]>;
}) {
  const { t } = useTranslation("methodSetup");
  const fresh = connections?.status === "ready" && !connections.refreshing && connections.refreshError === null;
  const connection = fresh ? connections.data.find(item => item.provider === provider) : undefined;
  return <div className="method-provider-notice">
    <p className="hint">{t(offered === null ? "discoveryUnknown" : offered ? "providerOffered" : "providerUnavailable", { provider })}</p>
    {(provider === "comgate" || provider === "packeta") && <>
      <p className="hint">{t(!fresh ? "accountUnknown" : !connection ? "accountMissing" : !connection.isActive ? "accountInactive" : !connection.hasSecret ? "credentialMissing" : "referenceOnly")}</p>
      {provider === "packeta" && fresh && !connection?.publishableKey && <p className="hint">{t("mapKeyMissing")}</p>}
      <Link to={`/stores/${storeId}/provider-connections`}>{t("connections")}</Link>
    </>}
    {provider === "stripe" && <p className="hint">{t("stripe")}</p>}
  </div>;
}
