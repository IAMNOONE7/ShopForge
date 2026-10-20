import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api } from "../api";
import { connectionFailure } from "../api/providerConnections";
import { useRequest } from "../useRequest";
import { LegacyPickupPoints } from "./LegacyPickupPoints";
import { PaymentMethodsSection } from "./PaymentMethodsSection";
import { ShippingMethodsSection } from "./ShippingMethodsSection";
import { StoreReadinessList } from "./StoreReadinessList";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

type Props = { storeId: string; canManage: boolean; storeStale: boolean; money: Intl.NumberFormat };
export function MethodsSection({ storeId, money, canManage, storeStale }: Props) {
  const { t } = useTranslation("methodSetup");
  const [refreshToken, setRefreshToken] = useState(0);
  const [connections, reloadConnections] = useRequest(`method-connections:${storeId}`, signal => api.providerConnections(storeId, signal));
  const [readiness, reloadReadiness] = useRequest(`method-readiness:${storeId}`, signal => api.storeReadiness(storeId, signal));
  const afterAttempt = useCallback(() => { reloadConnections(); reloadReadiness(); }, [reloadConnections, reloadReadiness]);
  const refresh = () => { setRefreshToken(current => current + 1); reloadConnections(); reloadReadiness(); };
  const connectionError = connections.status === "error" || connections.status === "not-found" ? connections.error : connections.status === "ready" ? connections.refreshError : null;
  const readinessError = readiness.status === "error" || readiness.status === "not-found" ? readiness.error : readiness.status === "ready" ? readiness.refreshError : null;
  const readinessStale = readiness.status !== "ready" || readiness.refreshing || readiness.refreshError !== null;
  return <div className="methods-workspace"><div className="section-heading"><p>{t("intro")}</p><Button type="button" variant="secondary" data-read-action onClick={refresh}>{t("refresh")}</Button></div>
    {storeStale && <InlineMessage>{t("storeStale")}</InlineMessage>}
    <div className="cluster"><Link to={`/stores/${storeId}/provider-connections`}>{t("connections")}</Link><Link to={`/stores/${storeId}/settings`}>{t("publication")}</Link></div>
    {connectionError !== null && <div className="method-connection-error"><RequestError error={connectionFailure(connectionError)} operation="read" onRetry={reloadConnections} /><p>{t("accountUnknown")}</p></div>}
    <PaymentMethodsSection storeId={storeId} allowed={canManage} storeStale={storeStale} refreshToken={refreshToken} connections={connections} onAttempt={afterAttempt} />
    <ShippingMethodsSection storeId={storeId} money={money} allowed={canManage} storeStale={storeStale} refreshToken={refreshToken} connections={connections} onAttempt={afterAttempt} />
    <section className="method-readiness" aria-labelledby="method-readiness-title"><h2 id="method-readiness-title">{t("readiness")}</h2><p className="hint">{t("readinessHint")}</p>
      {readinessStale && <InlineMessage>{t("readinessUnknown")}</InlineMessage>}
      {readinessError !== null && <RequestError error={connectionFailure(readinessError)} operation="read" onRetry={reloadReadiness} />}
      {readiness.status === "ready" && <StoreReadinessList storeId={storeId} checks={readiness.data.checks.filter(check => check.check.startsWith("orders."))} stale={readinessStale} />}
    </section>
    <LegacyPickupPoints storeId={storeId} allowed={canManage} storeStale={storeStale} />
  </div>;
}
