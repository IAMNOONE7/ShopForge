import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type ShippingMethod, type StoreProduct } from "../api";
import { parcelShippingApi, type Quote, type QuoteInput } from "../api/parcelShipping";
import { isHttpError } from "../api/http";
import { useRequest } from "../useRequest";
import { wholeNumber } from "./parcelValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { RequestError } from "./ui/RequestError";

type CartLine = { storeProductId: string; variantId: string; sku: string; name: string; quantity: string };
export function ShippingQuotePanel({ storeId, method, money, stale, onClose }: { storeId: string; method: ShippingMethod; money: Intl.NumberFormat; stale: boolean; onClose: () => void }) {
  const { t } = useTranslation("parcelShipping");
  const [query, setQuery] = useState(""), [search, setSearch] = useState({ q: "", page: 1 });
  const [listings, reloadListings] = useRequest("quote-listings:" + storeId + JSON.stringify(search), signal => api.storeProducts(storeId, signal, { ...search, pageSize: 25 }));
  const [forms, reloadForms] = useRequest("quote-forms:" + storeId, parcelShippingApi.forms);
  const [selected, setSelected] = useState<StoreProduct | null>(null), [variantId, setVariantId] = useState(""), [quantity, setQuantity] = useState("1");
  const [lines, setLines] = useState<CartLine[]>([]), [country, setCountry] = useState("");
  const [quote, setQuote] = useState<Quote | null>(null), [error, setError] = useState<unknown | null>(null), [pending, setPending] = useState(false), [invalid, setInvalid] = useState(false);
  const request = useRef<AbortController | null>(null), lock = useRef(false), generation = useRef(0), active = useRef(false), feedback = useRef<HTMLDivElement>(null), heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { active.current = true; heading.current?.focus(); const abort = () => request.current?.abort(); return () => { active.current = false; abort(); }; }, []);
  useEffect(() => { if (quote || error) feedback.current?.focus(); }, [quote, error]);
  function invalidate() { generation.current++; request.current?.abort(); request.current = null; lock.current = false; setPending(false); setQuote(null); setError(null); setInvalid(false); }
  const [previousStale, setPreviousStale] = useState(stale);
  if (previousStale !== stale) { setPreviousStale(stale); if (stale) { setQuote(null); setError(null); } }
  useEffect(() => { if (stale) request.current?.abort(); }, [stale]);
  const formsReady = forms.status === "ready" && !forms.refreshing && !forms.refreshError;
  const listingsReady = listings.status === "ready" && !listings.refreshing && !listings.refreshError;
  const variants = formsReady && selected ? forms.data.find(product => product.id === selected.productId)?.variants ?? [] : [];
  const available = listingsReady ? listings.data.items.filter(item => item.isVisible && item.archivedAt === null) : [];
  const listingError = listings.status === "ready" ? listings.refreshError : listings.status === "loading" ? null : listings.error;
  const formsError = forms.status === "ready" ? forms.refreshError : forms.status === "loading" ? null : forms.error;
  const currency = money.resolvedOptions().currency ?? "";
  async function preview(event: FormEvent) {
    event.preventDefault(); if (lock.current || stale) return;
    const valid = lines.length >= 1 && lines.length <= 200 && lines.every(line => wholeNumber(line.quantity, 99) !== null) && (!country.trim() || /^[a-z]{2}$/i.test(country.trim()));
    setInvalid(!valid); if (!valid) { window.requestAnimationFrame(() => feedback.current?.focus()); return; }
    const input: QuoteInput = { country: country.trim().toUpperCase() || null, lines: lines.map(line => ({ storeProductId: line.storeProductId, variantId: line.variantId, quantity: wholeNumber(line.quantity, 99)! })) };
    const current = ++generation.current, controller = new AbortController(); request.current = controller; lock.current = true; setPending(true); setQuote(null); setError(null);
    try {
      const result = await parcelShippingApi.quote(storeId, method, currency, input, controller.signal);
      if (active.current && current === generation.current && !controller.signal.aborted) setQuote(result);
    } catch (error) {
      if (active.current && current === generation.current && !controller.signal.aborted) setError(error);
    } finally { if (active.current && current === generation.current) { lock.current = false; request.current = null; setPending(false); } }
  }
  return <section className="shipping-quote-panel" aria-labelledby="shipping-quote-title">
    <div className="section-heading"><h3 ref={heading} tabIndex={-1} id="shipping-quote-title">{t("tryNamed", { name: method.name })}</h3><Button type="button" data-read-action variant="secondary" onClick={onClose}>{t("closeQuote")}</Button></div>
    <p>{t("quoteHint")}</p>{method.ratesFrom === "placeholder" && <p>{t("placeholderWarning")}</p>}{!method.isActive && <p>{t("inactiveQuote")}</p>}
    {stale && <p role="status">{t("quoteStale")}</p>}
    <form className="quote-search" onSubmit={event => { event.preventDefault(); if (!stale) { setSearch({ q: query.trim(), page: 1 }); setSelected(null); setVariantId(""); } }}>
      <Field data-read-action label={t("searchProducts")} value={query} maxLength={200} onChange={event => setQuery(event.target.value)} /><Button type="submit" data-read-action disabled={stale}>{t("search")}</Button>
    </form>
    {listingError !== null && <RequestError error={listingError} operation="read" onRetry={reloadListings} />}
    {formsError !== null && <RequestError error={formsError} operation="read" onRetry={reloadForms} />}
    {!formsReady && <p role="status">{t("formsLoading")}</p>}
    {!listingsReady && <p role="status">{t("productsLoading")}</p>}
    {listingsReady && <><ul className="quote-products">{available.map(item => <li key={item.id}><span>{item.name} · {item.sku}</span><Button type="button" data-read-action variant="secondary" disabled={stale} aria-label={t("chooseProduct", { name: item.name })} onClick={() => { setSelected(item); setVariantId(""); }}>{t("choose")}</Button></li>)}</ul>
      {!available.length && <p>{t("noProducts")}</p>}
      <div className="cluster"><Button type="button" data-read-action variant="secondary" disabled={stale || search.page <= 1} onClick={() => { setSearch({ ...search, page: search.page - 1 }); setSelected(null); }}>{t("previousProducts")}</Button><span>{t("productPage", { page: listings.data.page })}</span><Button type="button" data-read-action variant="secondary" disabled={stale || !listings.data.hasMore} onClick={() => { setSearch({ ...search, page: search.page + 1 }); setSelected(null); }}>{t("nextProducts")}</Button></div>
    </>}
    {selected && <div className="quote-selection"><h4>{selected.name}</h4><label htmlFor="quote-variant">{t("variant")}</label><select data-read-action id="quote-variant" value={variantId} disabled={stale || !formsReady} onChange={event => setVariantId(event.target.value)}><option value="">{t("chooseVariant")}</option>{variants.map(form => <option key={form.id} value={form.id}>{form.sku}{form.optionValues.length ? " · " + form.optionValues.join(" / ") : ""}</option>)}</select>
      <Field data-read-action label={t("quantity")} inputMode="numeric" value={quantity} disabled={stale} onChange={event => setQuantity(event.target.value)} />
      <Button type="button" data-read-action disabled={stale || !listingsReady || !formsReady || !variants.some(form => form.id === variantId) || !wholeNumber(quantity, 99) || lines.length >= 200 || lines.some(line => line.variantId === variantId)} onClick={() => {
        const variant = variants.find(form => form.id === variantId); if (!variant || !wholeNumber(quantity, 99)) return; invalidate(); setLines([...lines, { storeProductId: selected.id, variantId, sku: variant.sku, name: selected.name, quantity }]);
      }}>{t("addLine")}</Button>
    </div>}
    <form className="quote-cart" onSubmit={event => void preview(event)} noValidate>
      <h4>{t("cart")}</h4><p>{t("cartHint")}</p>
      <ul>{lines.map((line, index) => <li key={line.variantId}><strong>{line.name} · {line.sku}</strong><Field data-read-action label={t("lineQuantity", { number: index + 1 })} inputMode="numeric" value={line.quantity} disabled={stale} error={invalid && !wholeNumber(line.quantity, 99) ? t("cartInvalid") : undefined} onChange={event => { invalidate(); setLines(lines.map((item, at) => at === index ? { ...item, quantity: event.target.value } : item)); }} /><Button type="button" data-read-action variant="secondary" disabled={stale} onClick={() => { invalidate(); setLines(lines.filter((_, at) => at !== index)); }}>{t("removeLine", { number: index + 1 })}</Button></li>)}</ul>
      <Field data-read-action id="quote-country" name="country" label={t("country")} hint={t("countryHint")} maxLength={2} disabled={stale} value={country} onChange={event => { invalidate(); setCountry(event.target.value); }} />
      <Button type="submit" data-read-action disabled={stale} busy={pending} busyLabel={t("quoting")}>{t("quote")}</Button>
    </form>
    <div ref={feedback} tabIndex={-1} className="quote-feedback">
      {invalid && <p role="alert">{t("cartInvalid")}</p>}
      {error !== null && <><RequestError error={error} operation="read" fieldErrors={[]} />{isHttpError(error) && error.problem?.errors && <details><summary>{t("diagnostic")}</summary>{Object.entries(error.problem.errors).filter(([field]) => ["country", "lines"].includes(field.toLowerCase())).flatMap(([field, values]) => Array.isArray(values) ? values.filter(value => typeof value === "string").map((value, index) => <p key={field + index}>{value}</p>) : [])}</details>}</>}
      {quote && !stale && <QuoteResult quote={quote} money={money} />}
    </div>
  </section>;
}
function QuoteResult({ quote, money }: { quote: Quote; money: Intl.NumberFormat }) {
  const { t } = useTranslation("parcelShipping");
  return <div className="quote-result"><h4>{t("quoteResult")}</h4>{quote.unavailable ? <><p role="status">{t(`reasons.${quote.unavailable.reason}`)}</p><p>{quote.unavailable.detail}</p>{quote.unavailable.variantId && <p>{t("variant")}: <code>{quote.unavailable.variantId}</code></p>}</> : <>
    <p role={quote.charged === undefined ? "status" : undefined}>{t(quote.shippingDiscount ? "costBeforeDiscount" : "total")}: <strong>{money.format(quote.total)}</strong></p>
    {quote.shippingDiscount !== undefined && quote.shippingDiscount > 0 && <p>{t("shippingDiscount")}: <strong>{money.format(quote.shippingDiscount)}</strong></p>}
    {quote.freeShippingThreshold != null && <p>{t("freeShippingThreshold")}: <strong>{money.format(quote.freeShippingThreshold)}</strong></p>}
    {quote.charged !== undefined && <p role="status">{t("charged")}: <strong>{money.format(quote.charged)}</strong></p>}
    {quote.parcels.length === 0 && <p>{t("flatQuote")}</p>}
    <ol className="quoted-parcels">{quote.parcels.map((parcel, index) => <li key={index}><h5>{t("parcel", { number: index + 1 })}</h5><p>{parcel.grossWeightGrams} g · {t("bandUpTo", { grams: parcel.bandUpToGrams })} · {money.format(parcel.price)}</p><p>{t(parcel.alone ? "alone" : "together")}</p><ul>{parcel.contents.map((item, at) => <li key={at}>{item.sku} × {item.quantity}</li>)}</ul></li>)}</ol>
  </>}</div>;
}
