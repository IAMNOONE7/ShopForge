import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams } from "react-router";
import { api, type AttributeDefinition } from "../api";
import { useSelectedStore } from "../adminContext";
import { EditAttributeForm, NewAttributeForm } from "../components/AttributeForms";
import { AttributeOptions } from "../components/AttributeOptions";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
import { useRequest, type RequestState } from "../useRequest";

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
            <th scope="row"><Link to={item.id}>{item.name}</Link></th><td><code>{item.code}</code></td><td>{t(`types.${item.type}`)}</td><td>{t(item.isFilterable ? "yes" : "no")}</td><td>{t(item.isVisibleOnProductPage ? "yes" : "no")}</td><td>{item.sortOrder}</td>
          </tr>)}</tbody></table>
        <ul className="attribute-cards">{state.data.map((item) => <li key={item.id}>
          <Link to={item.id} className="attribute-card-link">{item.name}</Link>
          <dl><div><dt>{t("code")}</dt><dd><code>{item.code}</code></dd></div><div><dt>{t("type")}</dt><dd>{t(`types.${item.type}`)}</dd></div><div><dt>{t("filter")}</dt><dd>{t(item.isFilterable ? "yes" : "no")}</dd></div><div><dt>{t("shown")}</dt><dd>{t(item.isVisibleOnProductPage ? "yes" : "no")}</dd></div><div><dt>{t("sortOrder")}</dt><dd>{item.sortOrder}</dd></div></dl>
        </li>)}</ul>
      </>)}
  </div>;
}

export function NewAttributePage() {
  const { t } = useTranslation("attributes");
  const { store } = useSelectedStore();
  const navigate = useNavigate();
  return <div className="attribute-detail-page"><Link to={`/stores/${store.id}/attributes`} className="physical-back-link">{t("backToAttributes")}</Link>
    <h1>{t("newTitle")}</h1><p className="hint">{t("contentHint")}</p>
    <NewAttributeForm storeId={store.id} onCreated={(item) => void navigate(`/stores/${store.id}/attributes/${item.id}`)} />
  </div>;
}

export function AttributeDetailPage() {
  const { t } = useTranslation("attributes");
  const { user } = useSession();
  const { store } = useSelectedStore();
  const { attributeId } = useParams();
  const [state, reload] = useRequest(`attributes:${store.id}`, (signal) => api.attributes(store.id, signal));
  const item = state.status === "ready" ? state.data.find((value) => value.id === attributeId) : undefined;
  const allowed = canManageCatalog(user.role);
  return <div className="attribute-detail-page"><Link to={`/stores/${store.id}/attributes`} className="physical-back-link">{t("backToAttributes")}</Link>
    <h1>{item?.name ?? t("detailTitle")}</h1>
    <AttributesLoad state={state} reload={reload} />
    {state.status === "ready" && state.refreshError === null && (item ? <div className="attribute-detail-grid">
      {allowed ? <EditAttributeForm key={`${item.id}:edit`} storeId={store.id} definition={item} reload={reload} /> :
        <section className="physical-panel"><h2>{t("details")}</h2><dl className="attribute-readonly"><div><dt>{t("code")}</dt><dd><code>{item.code}</code></dd></div><div><dt>{t("type")}</dt><dd>{t(`types.${item.type}`)}</dd></div><div><dt>{t("unitOptional")}</dt><dd>{item.unit || "—"}</dd></div><div><dt>{t("filter")}</dt><dd>{t(item.isFilterable ? "yes" : "no")}</dd></div><div><dt>{t("shown")}</dt><dd>{t(item.isVisibleOnProductPage ? "yes" : "no")}</dd></div><div><dt>{t("sortOrder")}</dt><dd>{item.sortOrder}</dd></div></dl></section>}
      <AttributeOptions key={`${item.id}:options`} storeId={store.id} definition={item} allowed={allowed} reload={reload} />
    </div> : <EmptyState headingLevel={2} title={t("missingTitle")} action={<Link to={`/stores/${store.id}/attributes`}>{t("backToAttributes")}</Link>}>{t("missingBody")}</EmptyState>)}
  </div>;
}
