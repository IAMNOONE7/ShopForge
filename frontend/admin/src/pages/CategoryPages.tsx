import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams } from "react-router";
import { api, type Category } from "../api";
import { useSelectedStore } from "../adminContext";
import { CategoryEditor } from "../components/CategoryEditor";
import { CategoryForm } from "../components/CategoryForm";
import { categoryPath } from "../components/categoryTree";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest, type RequestState } from "../useRequest";
import { ForbiddenPage } from "./RouteStatePage";

function CategoriesRead({ state, reload }: { state: RequestState<Category[]>; reload: () => void }) {
  const { t } = useTranslation("categories");
  if (state.status === "loading") return <LoadingState label={t("loading")} lines={4} />;
  if (state.status !== "ready") return <RequestError error={state.error} operation="read" onRetry={reload} />;
  if (state.refreshError !== null) return <RequestError error={state.refreshError} operation="read" onRetry={reload} />;
  return null;
}

export function CategoriesPage() {
  const { t } = useTranslation("categories");
  const { store } = useSelectedStore();
  const { user } = useSession();
  const [state, reload] = useRequest(`categories:${store.id}`, (signal) => api.categories(store.id, signal));
  return <div className="category-page">
    <div className="page-heading-row"><div><h1>{t("title")}</h1><p className="hint">{t("listHint", { store: store.name })}</p></div>
      {canManageCatalog(user.role) && <Link className="button" to="new">{t("newCategory")}</Link>}
    </div>
    <CategoriesRead state={state} reload={reload} />
    {state.status === "ready" && (state.data.length === 0 ? <EmptyState headingLevel={2} title={t("emptyTitle")}>{t("emptyBody")}</EmptyState> : <>
      <p className="hint">{t("count", { count: state.data.length })}</p>
      <ul className="category-list">{state.data.map((category) => {
        const path = categoryPath(state.data, category.id);
        const missingParent = category.parentId && !state.data.some((candidate) => candidate.id === category.parentId) ? category.parentId : null;
        return <li key={category.id} className="category-list-row">
          <div>{path.length > 1 && <p className="category-parent-path">{path.slice(0, -1).map((part) => part.name).join(" › ")}</p>}
            {missingParent && <p className="category-parent-path">{t("unavailableParent", { id: missingParent })}</p>}
            <Link to={category.id}>{category.name}</Link><p className="hint"><code>{category.slug}</code></p></div>
          <dl><div><dt>{t("level")}</dt><dd>{path.length}</dd></div><div><dt>{t("sortOrder")}</dt><dd>{category.sortOrder}</dd></div>
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
  const [state, reload] = useRequest(`categories:${storeId}:${categoryId}`, (signal) => api.categories(storeId, signal));
  const [attributes, reloadAttributes] = useRequest(`category-attributes:${storeId}:${categoryId}`, (signal) => api.attributes(storeId, signal));
  const category = state.status === "ready" ? state.data.find((candidate) => candidate.id === categoryId) : undefined;
  const path = state.status === "ready" && category ? categoryPath(state.data, category.id) : [];
  return <div className="category-page"><Link className="physical-back-link" to={`/stores/${storeId}/categories`}>{t("back")}</Link>
    <div className="page-heading-row"><h1>{category?.name ?? t("details")}</h1>
      {state.status === "ready" && <Button type="button" variant="secondary" disabled={state.refreshing} onClick={reload}>{t("refresh")}</Button>}
    </div>
    <CategoriesRead state={state} reload={reload} />
    {state.status === "ready" && (category ? <>
      <nav className="category-breadcrumb" aria-label={t("ancestry")}><ol>{path.map((part) => <li key={part.id}>{part.id === category.id
        ? <span aria-current="page">{part.name}</span> : <Link to={`/stores/${storeId}/categories/${part.id}`}>{part.name}</Link>}</li>)}</ol></nav>
      <CategoryEditor storeId={storeId} category={category} categories={state.data} attributes={attributes} reloadAttributes={reloadAttributes}
        allowed={canManageCatalog(user.role)} refreshing={state.refreshing} refreshFailed={state.refreshError !== null} reload={reload} />
    </> : <EmptyState headingLevel={2} title={t("missingTitle")}><p>{t("missingBody")}</p></EmptyState>)}
  </div>;
}
