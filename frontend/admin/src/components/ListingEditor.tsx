import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api, type AttributeDefinition, type AttributeValues, type Category, type StoreProduct } from "../api";
import { useAction } from "../useAction";
import type { RequestState } from "../useRequest";
import { currencyFormatter, uiLocale } from "../utils/format";
import { ListingAttributeForm } from "./ListingAttributeForm";
import { ListingCategoryForm } from "./ListingCategoryForm";
import { ListingForm } from "./ListingForm";
import { attributeLabel } from "./listingAttributes";
import { categoryPath } from "./categoryTree";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

type Editor = { kind: "details" | "categories" | "values"; item: StoreProduct; categories: Category[]; definitions: AttributeDefinition[]; values: AttributeValues };
function ready<T>(state: RequestState<T>): state is Extract<RequestState<T>, { status: "ready" }> { return state.status === "ready" && !state.refreshing && state.refreshError === null; }

export function ListingRead<T>({ state, reload, label }: { state: RequestState<T>; reload: () => void; label: string }) {
  if (state.status === "loading") return <LoadingState label={label} lines={2} />;
  if (state.status !== "ready") return <RequestError error={state.error} operation="read" onRetry={reload} />;
  return state.refreshError !== null ? <RequestError error={state.refreshError} operation="read" onRetry={reload} /> : null;
}

export function ListingEditor({ storeId, currency, item, categories, definitions, values, reloadCategories, reloadDefinitions, reloadValues, reload, allowed, refreshing, refreshFailed }: {
  storeId: string; currency: string; item: StoreProduct; categories: RequestState<Category[]>; definitions: RequestState<AttributeDefinition[]>; values: RequestState<{ values: AttributeValues }>;
  reloadCategories: () => void; reloadDefinitions: () => void; reloadValues: () => void; reload: () => void; allowed: boolean; refreshing: boolean; refreshFailed: boolean;
}) {
  const { t, i18n } = useTranslation(["listings", "common"]);
  const [editor, setEditor] = useState<Editor | null>(null);
  const [saved, setSaved] = useState(false);
  const [attempted, setAttempted] = useState(false);
  const [error, run, pending] = useAction(reload);
  const root = useRef<HTMLElement>(null); const heading = useRef<HTMLHeadingElement>(null); const opener = useRef<HTMLButtonElement | null>(null);
  const categoriesReady = ready(categories); const valuesReady = ready(definitions) && ready(values);
  const stale = editor !== null && (JSON.stringify(editor.item) !== JSON.stringify(item)
    || (editor.kind === "categories" && categories.status === "ready" && JSON.stringify(editor.categories) !== JSON.stringify(categories.data))
    || (editor.kind === "values" && ((definitions.status === "ready" && JSON.stringify(editor.definitions) !== JSON.stringify(definitions.data))
      || (values.status === "ready" && JSON.stringify(editor.values) !== JSON.stringify(values.data.values)))));
  const disabled = !allowed || pending || refreshing || refreshFailed || stale;
  useEffect(() => { if (editor) (root.current?.querySelector<HTMLElement>("input") ?? root.current?.querySelector<HTMLElement>("h2"))?.focus(); }, [editor]);
  function open(kind: Editor["kind"], button: HTMLButtonElement) {
    if (disabled || editor || (kind === "categories" && !categoriesReady) || (kind === "values" && !valuesReady)) return;
    opener.current = button; setSaved(false); setAttempted(false);
    setEditor({ kind, item, categories: categories.status === "ready" ? categories.data : [], definitions: definitions.status === "ready" ? definitions.data : [], values: values.status === "ready" ? values.data.values : {} });
  }
  function close() { setEditor(null); setAttempted(false); window.requestAnimationFrame(() => opener.current?.focus()); }
  async function save(change: () => Promise<unknown>) {
    if (disabled || !editor || (editor.kind === "categories" && !categoriesReady) || (editor.kind === "values" && !valuesReady)) return;
    setAttempted(true); setSaved(false);
    await run(async () => { await change(); if (editor.kind === "values") reloadValues(); setEditor(null); setSaved(true); window.requestAnimationFrame(() => heading.current?.focus()); });
  }
  return <div className="listing-workspace">
    {saved && <InlineMessage tone="success">{t("listings:saved")}</InlineMessage>}
    {(refreshing || refreshFailed) && <InlineMessage>{t(refreshFailed ? "listings:refreshFailed" : "listings:refreshing")}</InlineMessage>}
    {stale && <InlineMessage>{t("listings:draftStale")}</InlineMessage>}
    {!allowed && <InlineMessage>{t("listings:readOnly")}</InlineMessage>}
    <section className="physical-panel" aria-labelledby="listing-details-title"><div className="listing-section-heading"><h2 id="listing-details-title" ref={heading} tabIndex={-1}>{t("listings:details")}</h2>
      {allowed && <Button type="button" variant="secondary" disabled={disabled || !!editor} onClick={(event) => open("details", event.currentTarget)}>{t("listings:editDetails")}</Button>}
    </div><dl className="listing-facts">
      <div><dt>{t("listings:slug")}</dt><dd><code>{item.slug}</code></dd></div><div><dt>{t("listings:price")}</dt><dd>{currencyFormatter(currency, i18n.resolvedLanguage).format(item.price)}</dd></div>
      <div><dt>{t("listings:vatRate")}</dt><dd>{new Intl.NumberFormat(uiLocale(i18n.resolvedLanguage)).format(item.vatRate)}%</dd></div><div><dt>{t("listings:sortOrder")}</dt><dd>{item.sortOrder}</dd></div><div><dt>{t("listings:visibility")}</dt><dd>{t(item.isVisible ? "listings:visible" : "listings:hidden")}</dd></div>
    </dl><p className="hint">{t("listings:priceHint")}</p>{item.description ? <div className="listing-description">{item.description.split(/\n\s*\n/).filter((part) => part.trim()).map((part, index) => <p key={index}>{part}</p>)}</div> : <p>{t("listings:noDescription")}</p>}</section>
    <section className="physical-panel" aria-labelledby="listing-categories-title"><div className="listing-section-heading"><h2 id="listing-categories-title">{t("listings:categories")}</h2>
      {allowed && <Button type="button" variant="secondary" disabled={disabled || !!editor || !categoriesReady} onClick={(event) => open("categories", event.currentTarget)}>{t("listings:editCategories")}</Button>}
    </div><ListingRead state={categories} reload={reloadCategories} label={t("listings:loadingCategories")} />
      {categories.status === "ready" && (item.categoryIds.length ? <ul>{item.categoryIds.map((id) => <li key={id}>{categories.data.some((category) => category.id === id)
        ? <Link to={`/stores/${storeId}/categories/${id}`}>{categoryPath(categories.data, id).map((part) => part.name).join(" › ")}</Link> : t("listings:unknownCategory", { id })}</li>)}</ul> : <p>{t("listings:noAssignedCategories")}</p>)}
    </section>
    <section className="physical-panel" aria-labelledby="listing-values-title"><div className="listing-section-heading"><h2 id="listing-values-title">{t("listings:values")}</h2>
      {allowed && <Button type="button" variant="secondary" disabled={disabled || !!editor || !valuesReady} onClick={(event) => open("values", event.currentTarget)}>{t("listings:editValues")}</Button>}
    </div><ListingRead state={definitions} reload={reloadDefinitions} label={t("listings:loadingAttributes")} /><ListingRead state={values} reload={reloadValues} label={t("listings:loadingValues")} />
      {definitions.status === "ready" && values.status === "ready" && (Object.keys(values.data.values).length ? <dl className="listing-value-facts">{Object.entries(values.data.values).map(([code, value]) => {
        const attribute = definitions.data.find((definition) => definition.code === code);
        const unsafe = attribute?.type === "integer" && typeof value === "number" && !Number.isSafeInteger(value);
        return <div key={code}><dt>{attribute?.name ?? code}{attribute?.unit ? ` (${attribute.unit})` : ""}</dt><dd>{unsafe ? t("listings:unsafeInteger") : attribute ? attributeLabel(attribute, value, t("common:yes"), t("common:no"), uiLocale(i18n.resolvedLanguage)) : String(value)}</dd></div>;
      })}</dl> : <p>{t("listings:noValues")}</p>)}
    </section>
    {editor && <section className="physical-panel listing-editor" ref={root}>
      {editor.kind === "details" ? <ListingForm item={editor.item} disabled={disabled} pending={pending} onCancel={close} onSave={(input) => save(() => api.updateStoreProduct(storeId, item.id, input))} />
        : editor.kind === "categories" ? <ListingCategoryForm item={editor.item} categories={editor.categories} disabled={disabled || !categoriesReady} pending={pending} onCancel={close} onSave={(ids) => save(() => api.assignCategories(storeId, item.id, ids))} />
          : <ListingAttributeForm attributes={editor.definitions} values={editor.values} disabled={disabled || !valuesReady} pending={pending} onCancel={close} onSave={(input) => save(() => api.setProductAttributes(storeId, item.id, input))} />}
      {attempted && error !== null && <RequestError error={error} operation="write" />}
    </section>}
  </div>;
}
