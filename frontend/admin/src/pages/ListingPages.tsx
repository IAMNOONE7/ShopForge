import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { api } from "../api";
import { useSelectedStore } from "../adminContext";
import { ListingEditor, ListingRead } from "../components/ListingEditor";
import { ListingForm } from "../components/ListingForm";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { Field } from "../components/ui/Field";
import { InlineMessage } from "../components/ui/InlineMessage";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { currencyFormatter } from "../utils/format";
import { ForbiddenPage } from "./RouteStatePage";

const sorts = ["default", "name", "-name", "price", "-price"] as const;
function searchParameters(page: number, q: string, sort: string) {
  const parameters = new URLSearchParams(); if (page > 1) parameters.set("page", String(page)); if (q) parameters.set("q", q); if (sort !== "default") parameters.set("sort", sort); return parameters;
}
function ListingSearch({ q, onSearch }: { q: string; onSearch: (q: string) => void }) {
  const { t } = useTranslation("listings"); const [text, setText] = useState(q); const [invalid, setInvalid] = useState(false);
  function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const value = text.trim(); if (value.length === 1) { setInvalid(true); document.getElementById("listing-search")?.focus(); return; } setInvalid(false); onSearch(value); }
  return <form className="listing-search" noValidate onSubmit={submit}><Field id="listing-search" name="q" label={t("search")} hint={t("searchHint")} error={invalid ? t("searchInvalid") : undefined} value={text} onChange={(event) => { setText(event.target.value); setInvalid(false); }} />
    <Button type="submit">{t("searchAction")}</Button>{q && <Button type="button" variant="secondary" onClick={() => onSearch("")}>{t("clearSearch")}</Button>}</form>;
}
export function ListingsPage() {
  const { t, i18n } = useTranslation("listings"); const { store } = useSelectedStore(); const { user } = useSession();
  const [parameters, setParameters] = useSearchParams(); const rawPage = parameters.get("page") ?? "1";
  const page = /^\d+$/.test(rawPage) && Number(rawPage) >= 1 && Number(rawPage) <= 100000 ? Number(rawPage) : 1;
  const typedQuery = (parameters.get("q") ?? "").trim(); const q = typedQuery.length >= 2 ? typedQuery : ""; const sort = sorts.find((key) => key === parameters.get("sort")) ?? "default";
  const [state, reload] = useRequest(`listings:${store.id}:${page}:${sort}:${q}`, (signal) => api.storeProducts(store.id, signal, { page, pageSize: 25, sort, q }));
  const query = searchParameters(page, q, sort).toString();
  return <div className="listing-page"><div className="page-heading-row"><div><h1>{t("title")}</h1><p className="hint">{t("listHint", { store: store.name })}</p></div>
    {canManageCatalog(user.role) && <Link className="button" to="new">{t("newListing")}</Link>}</div>
    {typedQuery.length === 1 && <InlineMessage>{t("searchInvalid")}</InlineMessage>}
    <section className="listing-list-controls"><ListingSearch key={q} q={q} onSearch={(next) => setParameters(searchParameters(1, next, sort))} />
      <div className="listing-field"><label htmlFor="listing-sort">{t("sort")}</label><select id="listing-sort" value={sort} onChange={(event) => setParameters(searchParameters(1, q, event.target.value))}>{sorts.map((key) => <option key={key} value={key}>{t(`sorts.${key}`)}</option>)}</select></div>
    </section><ListingRead state={state} reload={reload} label={t("loading")} />
    {state.status === "ready" && <><p className="hint">{t("resultCount", { count: state.data.totalCount })}</p>
      {state.data.items.length ? <ul className="listing-list">{state.data.items.map((item) => <li key={item.id} className="listing-list-row"><div><Link to={`${item.id}${query ? `?${query}` : ""}`}>{item.name}</Link><p className="hint"><code>{item.sku}</code> · {item.slug}</p></div>
        <dl className="listing-facts"><div><dt>{t("price")}</dt><dd>{currencyFormatter(store.currency, i18n.resolvedLanguage).format(item.price)}</dd></div><div><dt>{t("visibility")}</dt><dd>{t(item.isVisible ? "visible" : "hidden")}</dd></div><div><dt>{t("sortOrder")}</dt><dd>{item.sortOrder}</dd></div></dl></li>)}</ul>
        : <EmptyState headingLevel={2} title={t(q ? "noMatchesTitle" : page > 1 ? "emptyPageTitle" : "emptyTitle")}><p>{t(q ? "noMatchesBody" : page > 1 ? "emptyPageBody" : "emptyBody")}</p></EmptyState>}
      <nav className="listing-pagination" aria-label={t("paging")}><Button type="button" variant="secondary" disabled={page === 1 || state.refreshing} onClick={() => setParameters(searchParameters(page - 1, q, sort))}>{t("previous")}</Button>
        <span>{t("pageSummary", { page: state.data.page, pages: Math.max(1, Math.ceil(state.data.totalCount / state.data.pageSize)) })}</span><Button type="button" variant="secondary" disabled={!state.data.hasMore || state.refreshing} onClick={() => setParameters(searchParameters(page + 1, q, sort))}>{t("next")}</Button>
      </nav></>}
  </div>;
}

export function NewListingPage() { const { user } = useSession(); const { store } = useSelectedStore(); return canManageCatalog(user.role) ? <NewListingWorkspace key={store.id} storeId={store.id} /> : <ForbiddenPage />; }
function NewListingWorkspace({ storeId }: { storeId: string }) {
  const { t } = useTranslation("listings"); const navigate = useNavigate(); const [products, reload] = useRequest(`listing-products:${storeId}:new`, api.products);
  const active = useRef(true);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const [error, run, pending] = useAction(() => undefined);
  return <div className="listing-page"><Link className="physical-back-link" to={`/stores/${storeId}/products`}>{t("back")}</Link><h1>{t("newListing")}</h1><p className="hint">{t("createHint")}</p>
    <ListingRead state={products} reload={reload} label={t("loadingProducts")} />
    {products.status === "ready" && (products.data.length ? <section className="physical-panel listing-editor"><ListingForm products={products.data} disabled={pending || products.refreshing || products.refreshError !== null} pending={pending}
      onSave={(input, productId) => run(async () => { const item = await api.listProduct(storeId, productId, input); if (active.current) await navigate(`/stores/${storeId}/products/${item.id}`); })} />
      {error !== null && <RequestError error={error} operation="write" />}</section> : <EmptyState headingLevel={2} title={t("noProductsTitle")} action={<Link to="/products/new">{t("createPhysicalProduct")}</Link>}><p>{t("noProductsBody")}</p></EmptyState>)}
  </div>;
}

export function ListingDetailPage() { const { store } = useSelectedStore(); const { listingId = "" } = useParams(); return <ListingDetailWorkspace key={`${store.id}:${listingId}`} storeId={store.id} listingId={listingId} />; }
function ListingDetailWorkspace({ storeId, listingId }: { storeId: string; listingId: string }) {
  const { t } = useTranslation("listings"); const { store } = useSelectedStore(); const { user } = useSession(); const [parameters] = useSearchParams();
  const [state, reload] = useRequest(`listing:${storeId}:${listingId}`, (signal) => api.findStoreProduct(storeId, listingId, signal));
  const [categories, reloadCategories] = useRequest(`listing-categories:${storeId}:${listingId}`, (signal) => api.categories(storeId, signal));
  const [definitions, reloadDefinitions] = useRequest(`listing-definitions:${storeId}:${listingId}`, (signal) => api.attributes(storeId, signal));
  const [values, reloadValues] = useRequest(`listing-values:${storeId}:${listingId}`, (signal) => api.productAttributes(storeId, listingId, signal));
  const [products, reloadProducts] = useRequest(`listing-physical:${storeId}:${listingId}`, api.products);
  const item = state.status === "ready" ? state.data : null;
  const product = products.status === "ready" && item ? products.data.find((candidate) => candidate.id === item.productId) : undefined;
  return <div className="listing-page"><Link className="physical-back-link" to={`/stores/${storeId}/products${parameters.size ? `?${parameters}` : ""}`}>{t("back")}</Link>
    <div className="page-heading-row"><h1>{item?.name ?? t("details")}</h1>{state.status === "ready" && <Button type="button" variant="secondary" disabled={state.refreshing} onClick={() => { reload(); reloadCategories(); reloadDefinitions(); reloadValues(); reloadProducts(); }}>{t("refresh")}</Button>}</div>
    <ListingRead state={state} reload={reload} label={t("loadingDetail")} />
    {state.status === "ready" && (item ? <>
      <section className="physical-panel"><h2>{t("physicalContext")}</h2><p><Link to={`/products/${item.productId}`}>{t("physicalProduct", { sku: item.sku })}</Link></p><p className="hint">{t("variantHint")}</p>
        <ListingRead state={products} reload={reloadProducts} label={t("loadingProducts")} />{product && <ul className="listing-variants">{product.variants.map((variant) => <li key={variant.id}><strong>{variant.sku}</strong>{variant.optionValues.length > 0 && <span> · {product.optionNames.map((name, index) => `${name}: ${variant.optionValues[index]}`).join(" · ")}</span>} <Link to={`/stock/${variant.id}`}>{t("stockLink", { sku: variant.sku })}</Link></li>)}</ul>}
      </section><ListingEditor storeId={storeId} currency={store.currency} item={item} allowed={canManageCatalog(user.role)} refreshing={state.refreshing} refreshFailed={state.refreshError !== null}
        categories={categories} definitions={definitions} values={values} reloadCategories={reloadCategories} reloadDefinitions={reloadDefinitions} reloadValues={reloadValues} reload={reload} />
    </> : <EmptyState headingLevel={2} title={t("missingTitle")}><p>{t("missingBody")}</p></EmptyState>)}
  </div>;
}
