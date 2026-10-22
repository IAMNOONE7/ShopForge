import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useSelectedStore } from "../adminContext";
import { api, type FailedMessage } from "../api";
import type { SuppressedAddress } from "../api/operations";
import { MessageOperationsPanel } from "../components/MessageOperationsPanel";
import { InlineMessage } from "../components/ui/InlineMessage";
import { canManageStore, useSession } from "../session";
import { formatDateTime, formatNumber } from "../utils/format";

function MessageFacts({ row }: { row: FailedMessage }) {
  const { t, i18n } = useTranslation("operations");
  return <>
    <dl className="operation-facts">
      <div><dt>{t("id")}</dt><dd><code>{row.id}</code></dd></div>
      <div><dt>{t("created")}</dt><dd><time dateTime={row.createdAt}>{formatDateTime(row.createdAt, i18n.resolvedLanguage)}</time></dd></div>
      <div><dt>{t("attempts")}</dt><dd>{formatNumber(row.attempts, i18n.resolvedLanguage)}</dd></div>
    </dl>
    {row.error ? <details className="operation-diagnostic"><summary>{t("diagnostic")}</summary><p>{row.error}</p></details> : <p className="hint">{t("noDiagnostic")}</p>}
  </>;
}

function AddressFacts({ row }: { row: SuppressedAddress }) {
  const { t, i18n } = useTranslation("operations");
  const reason = row.reason === "Bounced" ? t("reasons.bounced") : row.reason === "Complained" ? t("reasons.complained") : t("reasons.other", { reason: row.reason });
  return <>
    <dl className="operation-facts">
      <div><dt>{t("id")}</dt><dd><code>{row.id}</code></dd></div>
      <div><dt>{t("reason")}</dt><dd>{reason}</dd></div>
      <div><dt>{t("suppressedAt")}</dt><dd><time dateTime={row.suppressedAt}>{formatDateTime(row.suppressedAt, i18n.resolvedLanguage)}</time></dd></div>
    </dl>
    {row.detail && <details className="operation-diagnostic"><summary>{t("providerDetail")}</summary><p>{row.detail}</p></details>}
  </>;
}

export function StoreOperationsPage() {
  const { t } = useTranslation(["operations", "auth", "navigation"]);
  const { user } = useSession();
  const { store, storeRefreshing, storeRefreshError } = useSelectedStore();
  const canManage = canManageStore(user.role);
  const scope = `${user.tenantId}:${user.id}:${user.role}:${store.id}`;
  const storeFresh = !storeRefreshing && storeRefreshError === null;
  return <div className="message-operations-page">
    <h1>{t("operations:title")}</h1>
    <p className="hint">{t("operations:storeScope", { store: store.name })}</p>
    <Link to={`/stores/${store.id}/setup`}>{t("navigation:setupProgress")}</Link>
    {!canManage && <InlineMessage>{t("auth:readOnly")}</InlineMessage>}
    {!storeFresh && <InlineMessage>{t("operations:storeStale")}</InlineMessage>}
    <MessageOperationsPanel key={`${scope}:messages`} kind="messages" scope={scope} canManage={canManage} storeFresh={storeFresh}
      load={signal => api.failedMessages(store.id, signal)} write={row => api.requeueMessage(store.id, row.id)}
      label={row => row.type} renderRecord={row => <MessageFacts row={row} />} />
    <MessageOperationsPanel key={`${scope}:suppressed`} kind="suppressed" scope={scope} canManage={canManage} storeFresh={storeFresh}
      load={signal => api.suppressedAddresses(store.id, signal)} write={row => api.allowSuppressedAddress(store.id, row.id)}
      label={row => row.email} renderRecord={row => <AddressFacts row={row} />} />
  </div>;
}
