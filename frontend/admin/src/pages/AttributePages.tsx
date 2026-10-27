import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams } from "react-router";
import { api, type AttributeDefinition } from "../api";
import { useSelectedStore } from "../adminContext";
import { NewAttributeForm } from "../components/AttributeForms";
import { AttributeWorkspace } from "../components/AttributeWorkspace";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
import { useRequest, type RequestState } from "../useRequest";
import { ForbiddenPage } from "./RouteStatePage";

function AttributesLoad({ state, reload }: { state: RequestState<AttributeDefinition[]>; reload: () => void }) {
  const { t } = useTranslation("attributes");
  if (state.status === "loading") return <LoadingState label={t("loading")} lines={4} />;
  if (state.status !== "ready") return <RequestError error={state.error} operation="read" onRetry={reload} />;
  if (state.refreshError !== null) return <RequestError error={state.refreshError} operation="read" onRetry={reload} />;
  if (state.refreshing) return <p role="status" className="hint">{t("refreshing")}</p>;
  return null;
}

export function AttributesIndexPage() {
  const { t } = useTranslation("attributes");
  const { user } = useSession();
  const { store } = useSelectedStore();
  const [state, reload] = useRequest(`attributes:${store.id}`, (signal) => api.attributes(store.id, signal));
  const allowed = canManageCatalog(user.role);
  return <div className="attribute-page">
    <div className="page-heading-row"><div><h1>{t("title")}</h1><p className="hint">{t("listHint")}</p></div>
      {allowed && <Link className="button" to="new">{t("create")}</Link>}
    </div>
    <AttributesLoad state={state} reload={reload} />
    {state.status === "ready" && (state.data.length === 0 ?
      <EmptyState headingLevel={2} title={t("emptyTitle")} action={allowed ? <Link to="new">{t("create")}</Link> : undefined}>{t("emptyBody")}</EmptyState> :
      <>
        <p className="hint">{t("count", { count: state.data.length })}</p>
        <table className="attribute-table"><thead><tr><th scope="col">{t("name")}</th><th scope="col">{t("code")}</th><th scope="col">{t("type")}</th><th scope="col">{t("filter")}</th><th scope="col">{t("shown")}</th><th scope="col">{t("sortOrder")}</th></tr></thead>
          <tbody>{state.data.map((item) => <tr key={item.id}>
            <th scope="row"><Link to={encodeURIComponent(item.id)}>{item.name}</Link><span className="attribute-index-flags hint">{t("inFeeds")}: {t(item.isInFeeds ? "yes" : "no")} · {t("searchable")}: {t(item.isSearchable ? "yes" : "no")}</span></th><td><code>{item.code}</code></td><td>{t(`types.${item.type}`)}</td><td>{t(item.isFilterable ? "yes" : "no")}</td><td>{t(item.isVisibleOnProductPage ? "yes" : "no")}</td><td>{item.sortOrder}</td>
          </tr>)}</tbody></table>
        <ul className="attribute-cards">{state.data.map((item) => <li key={item.id}>
          <Link to={encodeURIComponent(item.id)} className="attribute-card-link">{item.name}</Link><p className="hint">{t("inFeeds")}: {t(item.isInFeeds ? "yes" : "no")} · {t("searchable")}: {t(item.isSearchable ? "yes" : "no")}</p>
          <dl><div><dt>{t("code")}</dt><dd><code>{item.code}</code></dd></div><div><dt>{t("type")}</dt><dd>{t(`types.${item.type}`)}</dd></div><div><dt>{t("filter")}</dt><dd>{t(item.isFilterable ? "yes" : "no")}</dd></div><div><dt>{t("shown")}</dt><dd>{t(item.isVisibleOnProductPage ? "yes" : "no")}</dd></div><div><dt>{t("sortOrder")}</dt><dd>{item.sortOrder}</dd></div></dl>
        </li>)}</ul>
      </>)}
  </div>;
}

export function NewAttributePage() {
  const { t } = useTranslation("attributes");
  const { user } = useSession();
  const { store, storeRefreshing, storeRefreshError } = useSelectedStore();
  const navigate = useNavigate();
  if (!canManageCatalog(user.role)) return <ForbiddenPage />;
  return <div className="attribute-detail-page"><Link to={`/stores/${store.id}/attributes`} className="physical-back-link">{t("backToAttributes")}</Link>
    <h1>{t("newTitle")}</h1><p className="hint">{t("contentHint")}</p>
    <NewAttributeForm key={JSON.stringify([store, user.tenantId, user.id, user.role])} storeId={store.id} blocked={Boolean(storeRefreshing || storeRefreshError)} onCreated={(item) => void navigate(`/stores/${encodeURIComponent(store.id)}/attributes/${encodeURIComponent(item.id)}`)} />
  </div>;
}

export function AttributeDetailPage() {
  const { user } = useSession();
  const { store, storeRefreshing, storeRefreshError } = useSelectedStore();
  const { attributeId = "" } = useParams();
  return <AttributeWorkspace key={JSON.stringify([user.tenantId, user.id, user.role, store, attributeId])}
    storeId={store.id} storeName={store.name} attributeId={attributeId} allowed={canManageCatalog(user.role)}
    storeStale={Boolean(storeRefreshing || storeRefreshError)} />;
}
