import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams, useSearchParams } from "react-router";
import { useSelectedStore } from "../adminContext";
import { api } from "../api";
import { OrderDetailsSection } from "../components/OrderDetailsSection";
import { OrderStatus } from "../components/OrderStatus";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { Field } from "../components/ui/Field";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useRequest } from "../useRequest";
import { formatDateTime, formatNumber } from "../utils/format";
import { maxOrderPage, orderListParameters, orderListQuery, orderSorts, type OrderListQuery } from "../utils/orderListQuery";

function OrderSearch({ q, onSearch }: { q: string; onSearch: (query: string) => void }) {
  const { t } = useTranslation("orders");
  const [draft, setDraft] = useState(q);
  const [invalid, setInvalid] = useState(false);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = draft.trim();
    if (next.length === 1) { setInvalid(true); document.getElementById("order-search")?.focus(); return; }
    setInvalid(false); onSearch(next);
  }
  return <form className="order-search" role="search" aria-label={t("search")} noValidate onSubmit={submit}>
    <Field id="order-search" name="q" type="search" label={t("search")} hint={t("searchHint")} value={draft}
      error={invalid ? t("searchInvalid") : undefined} onChange={(event) => { setDraft(event.target.value); setInvalid(false); }} />
    <div className="cluster">
      <Button type="submit">{t("searchAction")}</Button>
      {(q || draft) && <Button type="button" variant="secondary" onClick={() => { setDraft(""); setInvalid(false); onSearch(""); document.getElementById("order-search")?.focus(); }}>{t("clearSearch")}</Button>}
    </div>
  </form>;
}

export function StoreOrdersPage() {
  const { store } = useSelectedStore();
  return <OrdersWorkspace key={store.id} storeId={store.id} storeName={store.name} />;
}

function OrdersWorkspace({ storeId, storeName }: { storeId: string; storeName: string }) {
  const { t, i18n } = useTranslation(["orders", "exports"]);
  const { user } = useSession();
  const [parameters, setParameters] = useSearchParams();
  const { page, q, sort, shortQuery, invalidOptions } = orderListQuery(parameters);
  const query = orderListParameters({ page, q, sort });
  const searchFocus = useRef(false);
  useEffect(() => {
    if (searchFocus.current) { document.getElementById("order-search")?.focus(); searchFocus.current = false; }
  }, [q]);
  const [state, reload] = useRequest(`orders:${storeId}:${page}:${sort}:${q}`, (signal) => api.orders(storeId, signal, { page, q, sort }));
  const summary = useRef<HTMLParagraphElement>(null);
  const pagingFocus = useRef(false);
  useEffect(() => {
    if (state.status === "ready" && pagingFocus.current) {
      pagingFocus.current = false; summary.current?.focus();
    }
  }, [state]);
  function navigate(next: OrderListQuery, focus = false) {
    const nextParameters = orderListParameters(next);
    if (parameters.toString() === nextParameters.toString()) return;
    pagingFocus.current = focus;
    setParameters(nextParameters);
  }
  function search(next: string) {
    searchFocus.current = next !== q;
    navigate({ page: 1, q: next, sort });
  }
  const ready = state.status === "ready" ? state : null;
  const empty = ready?.data.items.length === 0;
  const emptyKind = page > 1 ? "outOfRange" : q ? "noMatches" : "noOrders";
  const refreshing = state.status === "loading" || ready?.refreshing === true;
  const suffix = query.size ? `?${query}` : "";
  return <div className="orders-page">
    <div className="order-page-heading"><div><h1>{t("title")}</h1><p className="hint">{t("listHint", { store: storeName })}</p></div>
      <Button type="button" variant="secondary" disabled={refreshing} onClick={reload}>{t("refresh")}</Button></div>
    {canManageStore(user.role) && <Link to={`/stores/${storeId}/exports`}>{t("exports:options")}</Link>}
    <section className="order-list-controls" aria-label={t("listControls")}>
      <OrderSearch key={q} q={q} onSearch={search} />
      <div className="order-sort"><label htmlFor="order-sort">{t("sort")}</label>
        <select id="order-sort" value={sort} onChange={(event) => navigate({ page: 1, q, sort: event.target.value as OrderListQuery["sort"] })}>
          {orderSorts.map((value) => <option key={value} value={value}>{t(`sorts.${value}`)}</option>)}
        </select></div>
    </section>
    {shortQuery && <InlineMessage>{t("searchInvalid")} <Link to={`/stores/${storeId}/orders${sort !== "-placed" ? `?sort=${sort}` : ""}`}>{t("clearSearch")}</Link></InlineMessage>}
    {invalidOptions && <InlineMessage>{t("invalidOptions")} <Link to={`/stores/${storeId}/orders`}>{t("resetOptions")}</Link></InlineMessage>}
    {state.status === "loading" && <LoadingState label={t("loadingList")} lines={4} />}
    {(state.status === "error" || state.status === "not-found") && <RequestError error={state.error} operation="read" onRetry={reload} />}
    {ready && <>
      <div className="order-results-heading"><h2 className="sr-only">{t("results")}</h2>
        <p ref={summary} tabIndex={-1} role="status">{t("resultRange", {
          from: formatNumber(ready.data.items.length ? (ready.data.page - 1) * ready.data.pageSize + 1 : 0, i18n.resolvedLanguage),
          to: formatNumber(ready.data.items.length ? (ready.data.page - 1) * ready.data.pageSize + ready.data.items.length : 0, i18n.resolvedLanguage),
          total: formatNumber(ready.data.totalCount, i18n.resolvedLanguage),
        })}</p>
        {ready.refreshing && <p role="status">{t("refreshing")}</p>}
      </div>
      {ready.refreshError !== null && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
      {(ready.refreshing || ready.refreshError !== null) && <p className="hint">{t("staleList")}</p>}
      <p className="hint">{t("detailsHint")}</p>
      {empty ? <EmptyState headingLevel={2} title={t(`${emptyKind}Title`)} action={page > 1
        ? <Button type="button" variant="secondary" onClick={() => navigate({ page: 1, q, sort }, true)}>{t("firstPageAction")}</Button>
        : q ? <Button type="button" variant="secondary" onClick={() => search("")}>{t("clearSearch")}</Button> : undefined}>
        <p>{t(`${emptyKind}Body`)}</p>
      </EmptyState> : <ul className="order-list" aria-label={t("title")} aria-busy={ready.refreshing}>
        {ready.data.items.map((order) => <li key={order.number} className="order-list-row">
          <div className="order-row-heading"><Link to={`${encodeURIComponent(order.number)}${suffix}`}>{order.number}</Link><OrderStatus status={order.status} /></div>
          <dl className="order-row-facts">
            <div><dt>{t("customer")}</dt><dd>{order.email}{!order.hasAccount && <span className="hint"> ({t("guest")})</span>}</dd></div>
            <div><dt>{t("placed")}</dt><dd><time dateTime={order.placedAt}>{formatDateTime(order.placedAt, i18n.resolvedLanguage)}</time></dd></div>
            <div><dt>{t("items")}</dt><dd>{formatNumber(order.items, i18n.resolvedLanguage)}</dd></div>
          </dl>
        </li>)}
      </ul>}
      <nav className="order-pagination" aria-label={t("paging")}>
        <Button type="button" variant="secondary" disabled={page === 1 || ready.refreshing} onClick={() => navigate({ page: page - 1, q, sort }, true)}>{t("previous")}</Button>
        <span>{t("pageSummary", { page: formatNumber(ready.data.page, i18n.resolvedLanguage), pages: formatNumber(Math.max(1, Math.ceil(ready.data.totalCount / ready.data.pageSize)), i18n.resolvedLanguage) })}</span>
        <Button type="button" variant="secondary" disabled={!ready.data.hasMore || page >= maxOrderPage || ready.refreshing} onClick={() => navigate({ page: page + 1, q, sort }, true)}>{t("next")}</Button>
      </nav>
    </>}
  </div>;
}

export function StoreOrderPage() {
  const { t } = useTranslation("orders");
  const { store } = useSelectedStore();
  const { user } = useSession();
  const { number = "" } = useParams();
  const [parameters] = useSearchParams();
  const query = orderListParameters(orderListQuery(parameters));
  return <div className="orders-page">
    <Link className="physical-back-link" to={`/stores/${store.id}/orders${query.size ? `?${query}` : ""}`}>{t("backToOrders")}</Link>
    <OrderDetailsSection key={`${store.id}:${number}`} storeId={store.id} userId={user.id} number={number} canManage={canManageStore(user.role)} />
  </div>;
}
