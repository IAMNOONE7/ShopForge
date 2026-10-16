import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { api } from "../api";
import { useSelectedStore } from "../adminContext";
import { CatalogChangeNotice, CatalogCollections } from "../components/CatalogRetirement";
import { CategoryEditor } from "../components/CategoryEditor";
import { CategoryForm } from "../components/CategoryForm";
import { categoryPath, unavailableCategoryAncestor } from "../components/categoryTree";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest, type RequestState } from "../useRequest";
import { ForbiddenPage } from "./RouteStatePage";

function CategoriesRead({ state, reload }: { state: RequestState<unknown>; reload: () => void }) {
  const { t } = useTranslation("categories");
  if (state.status === "loading") return <LoadingState label={t("loading")} lines={4} />;
  if (state.status !== "ready") return <RequestError error={state.error} operation="read" onRetry={reload} />;
  if (state.refreshError !== null) return <RequestError error={state.refreshError} operation="read" onRetry={reload} />;
  return null;
}

export function CategoriesPage() {
  const { t } = useTranslation(["categories", "retirement"]);
  const { store } = useSelectedStore();
  const { user } = useSession();
  const [parameters] = useSearchParams();
  const archived = parameters.get("archived") === "true";
  const [state, reload] = useRequest(`categories:${store.id}:${archived}`, (signal) => archived ? api.categories(store.id, signal, true) : api.categories(store.id, signal));
  return <div className="category-page">
    <div className="page-heading-row"><div><h1>{t("title")}</h1><p className="hint">{t("listHint", { store: store.name })}</p></div>
      {canManageCatalog(user.role) && <Link className="button" to="new">{t("newCategory")}</Link>}
    </div>
    <CatalogChangeNotice storeId={store.id} kind="categories" />
    <CatalogCollections archived={archived} activeUrl="?" archivedUrl="?archived=true" />
    {archived && <InlineMessage>{t("retirement:archivedListHint")}</InlineMessage>}
    <CategoriesRead state={state} reload={reload} />
    {state.status === "ready" && (state.data.length === 0 ? <EmptyState headingLevel={2} title={t(archived ? "retirement:emptyArchived" : "emptyTitle")}>{t(archived ? "retirement:emptyArchivedBody" : "emptyBody")}</EmptyState> : <>
      <p className="hint">{t("count", { count: state.data.length })}</p>
      <ul className="category-list">{state.data.map((category) => {
        const path = categoryPath(state.data, category.id);
        const missingParent = unavailableCategoryAncestor(state.data, category.id);
        return <li key={category.id} className="category-list-row">
          <div>{path.length > 1 && <p className="category-parent-path">{path.slice(0, -1).map((part) => part.name).join(" › ")}</p>}
            {missingParent && <p className="category-parent-path">{t("unavailableParent", { id: missingParent })}</p>}
            <Link to={`${category.id}${archived ? "?archived=true" : ""}`}>{category.name}</Link><p className="hint"><code>{category.slug}</code></p></div>
          <dl><div><dt>{t("level")}</dt><dd>{missingParent ? t("unknownLevel") : path.length}</dd></div><div><dt>{t("sortOrder")}</dt><dd>{category.sortOrder}</dd></div>
            <div><dt>{t("assignedAttributes")}</dt><dd>{category.attributeIds.length}</dd></div></dl>
        </li>;
      })}</ul>
    </>)}
  </div>;
}

export function NewCategoryPage() {
  const { user } = useSession();
  const { store } = useSelectedStore();
  if (!canManageCatalog(user.role)) return <ForbiddenPage />;
  return <NewCategoryWorkspace key={store.id} storeId={store.id} />;
}

function NewCategoryWorkspace({ storeId }: { storeId: string }) {
  const { t } = useTranslation("categories");
  const navigate = useNavigate();
  const [state, reload] = useRequest(`categories:${storeId}:new`, (signal) => api.categories(storeId, signal));
  const [error, run, pending] = useAction(() => undefined);
  return <div className="category-page"><Link className="physical-back-link" to={`/stores/${storeId}/categories`}>{t("back")}</Link>
    <h1>{t("newCategory")}</h1><p className="hint">{t("createHint")}</p>
    <CategoriesRead state={state} reload={reload} />
    {state.status === "ready" && <section className="physical-panel category-editor">
      <CategoryForm categories={state.data} disabled={pending || state.refreshing || state.refreshError !== null} pending={pending}
        onSave={(input) => run(async () => {
          const category = await api.createCategory(storeId, input);
          await navigate(`/stores/${storeId}/categories/${category.id}`);
        })} />
      {error !== null && <RequestError error={error} operation="write" />}
    </section>}
  </div>;
}

export function CategoryDetailPage() {
  const { store } = useSelectedStore();
  const { categoryId = "" } = useParams();
  return <CategoryDetailWorkspace key={`${store.id}:${categoryId}`} storeId={store.id} categoryId={categoryId} />;
}

function CategoryDetailWorkspace({ storeId, categoryId }: { storeId: string; categoryId: string }) {
  const { t } = useTranslation("categories");
  const { user } = useSession();
  const [parameters] = useSearchParams();
  const [state, reload] = useRequest(`categories:${storeId}:${categoryId}`, async (signal) => {
    const active = await api.categories(storeId, signal);
    const category = active.find((candidate) => candidate.id === categoryId);
    if (category) return { categories: active, category, archived: false };
    const archived = await api.categories(storeId, signal, true);
    return { categories: archived, category: archived.find((candidate) => candidate.id === categoryId), archived: true };
  });
  const [attributes, reloadAttributes] = useRequest(`category-attributes:${storeId}:${categoryId}`, (signal) => api.attributes(storeId, signal));
  const category = state.status === "ready" ? state.data.category : undefined;
  const path = state.status === "ready" && category ? categoryPath(state.data.categories, category.id) : [];
  const missingAncestor = state.status === "ready" && category ? unavailableCategoryAncestor(state.data.categories, category.id) : null;
  return <div className="category-page"><Link className="physical-back-link" to={`/stores/${storeId}/categories${parameters.get("archived") === "true" ? "?archived=true" : ""}`}>{t("back")}</Link>
    <div className="page-heading-row"><h1>{category?.name ?? t("details")}</h1>
      {state.status === "ready" && <Button type="button" variant="secondary" disabled={state.refreshing} onClick={reload}>{t("refresh")}</Button>}
    </div>
    <CategoriesRead state={state} reload={reload} />
    {state.status === "ready" && (category ? <>
      {missingAncestor && <InlineMessage>{t("unavailableParent", { id: missingAncestor })}</InlineMessage>}
      <nav className="category-breadcrumb" aria-label={t("ancestry")}><ol>{path.map((part) => <li key={part.id}>{part.id === category.id
        ? <span aria-current="page">{part.name}</span> : <Link to={`/stores/${storeId}/categories/${part.id}`}>{part.name}</Link>}</li>)}</ol></nav>
      <CategoryEditor storeId={storeId} category={category} categories={state.data.categories} archived={state.data.archived} attributes={attributes} reloadAttributes={reloadAttributes}
        allowed={canManageCatalog(user.role)} refreshing={state.refreshing} refreshFailed={state.refreshError !== null} reload={reload} />
    </> : <EmptyState headingLevel={2} title={t("missingTitle")}><p>{t("missingBody")}</p></EmptyState>)}
  </div>;
}
