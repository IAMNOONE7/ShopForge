import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { StoreProduct } from "../api";
import { maxBulkListings } from "../api/bulkListings";
import { fieldIssues } from "../api/errors";
import type { SelectedListing, useBulkListings } from "../useBulkListings";
import { bulkPriceDecimals } from "../utils/bulkListings";
import { uiLocale } from "../utils/format";
import { ListingBulkForm } from "./ListingBulkForm";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";
import { listingVisibilityMessages } from "./listingVisibility";

function SelectedListings({ listings, storeId, archived }: { listings: SelectedListing[]; storeId: string; archived: boolean }) {
  const { t } = useTranslation("bulkListings");
  return <details className="bulk-selected"><summary>{t("selectedRecords", { count: listings.length })}</summary><p className="hint">{t("savedNames")}</p>
    <ul tabIndex={0} aria-label={t("selectedRecords", { count: listings.length })}>{listings.map(listing => <li key={listing.id}><Link to={`/stores/${storeId}/products/${listing.id}${archived ? "?archived=true" : ""}`}>{listing.name}</Link> <code>{listing.sku}</code></li>)}</ul>
  </details>;
}

export function ListingBulkPanel({ bulk, storeId, currency, archived, items }: {
  bulk: ReturnType<typeof useBulkListings>; storeId: string; currency: string; archived: boolean; items: StoreProduct[];
}) {
  const { t, i18n } = useTranslation("bulkListings");
  const [verifiedIntent, setVerifiedIntent] = useState<typeof bulk.confirmation>(null);
  const confirmationTitle = useRef<HTMLHeadingElement>(null);
  const result = useRef<HTMLDivElement>(null);
  const selectedHere = items.filter(item => bulk.selection.has(item.id)).length;
  const allHere = items.length > 0 && items.every(item => bulk.selection.has(item.id));
  const moreThanLimit = bulk.selection.size + new Set(items.filter(item => !bulk.selection.has(item.id)).map(item => item.id)).size > maxBulkListings;
  useEffect(() => { if (bulk.confirmation) confirmationTitle.current?.focus(); }, [bulk.confirmation]);
  useEffect(() => { if (bulk.outcome) result.current?.focus(); }, [bulk.outcome]);
  function cancel() { bulk.cancel(); window.requestAnimationFrame(() => document.getElementById("bulk-action")?.focus()); }

  const intent = bulk.confirmation;
  const verified = intent !== null && verifiedIntent === intent;
  const outcome = bulk.outcome;
  const visibilityMessages = outcome?.kind === "refused" && outcome.intent.action === "show" ? listingVisibilityMessages(outcome.error, "storeProductIds") : [];
  const serverErrors = outcome && outcome.kind !== "success" ? [...new Map(fieldIssues(outcome.error).map(issue => {
    const selection = issue.field.toLowerCase().startsWith("storeproductids");
    const category = issue.field.toLowerCase().includes("category");
    const price = ["set", "bypercent", "price"].includes(issue.field.toLowerCase());
    const tax = issue.field.toLowerCase() === "taxclass";
    const field = outcome.kind === "uncertain" ? "bulk-outcome" : selection ? "bulk-selection" : category ? "bulk-categories" : price ? "bulk-value" : tax ? "bulk-taxClass" : "bulk-action";
    return [field, { field, message: t(selection ? "serverSelection" : category ? "serverCategories" : price ? "serverPrice" : tax ? "serverTaxClass" : "serverAction") }] as const;
  })).values()] : [];
  return <section className="physical-panel listing-bulk-panel" aria-labelledby="bulk-title">
    <div className="listing-section-heading"><h2 id="bulk-title">{t("title")}</h2><span role="status">{t("selectedCount", { count: bulk.selection.size, max: maxBulkListings })}</span></div>
    <p className="hint">{t("selectionHint", { max: maxBulkListings })}</p>
    <div className="catalog-action-row"><Button type="button" variant="secondary" disabled={bulk.locked || !items.length || !allHere && moreThanLimit} onClick={() => bulk.toggle(items, !allHere)}>{t(allHere ? "deselectPage" : "selectPage")}</Button>
      <Button type="button" variant="secondary" disabled={bulk.locked || !bulk.selection.size} onClick={bulk.clear}>{t("clear")}</Button></div>
    {bulk.selection.size === maxBulkListings && <InlineMessage>{t("limit", { max: maxBulkListings })}</InlineMessage>}
    {bulk.selection.size > selectedHere && <p className="hint">{t("elsewhere", { count: bulk.selection.size - selectedHere })}</p>}
    {outcome && <div id="bulk-outcome" className="bulk-outcome" ref={result} tabIndex={-1}>
      {outcome.kind === "success" ? <><InlineMessage tone="success" title={t("complete")}>{t("result", outcome.result)}</InlineMessage><p>{t("countMeaning")}</p></>
        : <><RequestError error={outcome.error} operation="write" fieldErrors={serverErrors} /><InlineMessage tone={outcome.kind === "uncertain" ? "error" : "info"}>{t(outcome.kind === "uncertain" ? "uncertain" : "refused")}</InlineMessage></>}
      {outcome.kind !== "refused" && <SelectedListings listings={outcome.intent.listings} storeId={storeId} archived={archived} />}
      {outcome.kind === "uncertain" && <Button type="button" variant="secondary" disabled={bulk.pending || !bulk.ready} onClick={bulk.finishReview}>{t("finishReview")}</Button>}
    </div>}
    {bulk.selection.size > 0 && outcome?.kind !== "uncertain" && <>
      <div id="bulk-selection" tabIndex={-1}><SelectedListings listings={[...bulk.selection.values()]} storeId={storeId} archived={archived} /></div>
      <ListingBulkForm storeId={storeId} currency={currency} archived={archived} disabled={bulk.locked} hidden={intent !== null} revision={bulk.revision} visibilityMessages={visibilityMessages} onReview={bulk.prepare} />
    </>}
    {intent && <form className="bulk-confirmation" aria-labelledby="bulk-confirm-title" onSubmit={event => { event.preventDefault(); if (verified) void bulk.confirm(); }} onKeyDown={event => {
      if (event.key === "Escape") { event.preventDefault(); if (!bulk.pending) cancel(); }
    }}>
      <h3 id="bulk-confirm-title" ref={confirmationTitle} tabIndex={-1}>{t("confirmTitle", { action: t(`actions.${intent.action}`), count: intent.listings.length })}</h3>
      <p>{t(`hints.${intent.action}`)}</p>
      {intent.change.action === "price" && <p><strong>{"set" in intent.change && intent.change.set !== undefined ? new Intl.NumberFormat(uiLocale(i18n.resolvedLanguage), { style: "currency", currency, minimumFractionDigits: bulkPriceDecimals(currency), maximumFractionDigits: bulkPriceDecimals(currency) }).format(intent.change.set)
        : `${new Intl.NumberFormat(uiLocale(i18n.resolvedLanguage), { maximumFractionDigits: 6 }).format(intent.change.byPercent!)}%`}</strong></p>}
      {intent.change.action === "categories" && <dl className="bulk-category-summary"><div><dt>{t("addCategory")}</dt><dd>{intent.categoryNames.add.join(" · ") || t("none")}</dd></div><div><dt>{t("removeCategory")}</dt><dd>{intent.categoryNames.remove.join(" · ") || t("none")}</dd></div></dl>}
      {intent.change.action === "tax-class" && <p><strong>{intent.change.taxClass === null ? t("clearTaxClass") : `${intent.taxClassName ?? intent.change.taxClass} (${intent.change.taxClass})`}</strong></p>}
      <p>{t("confirmHint")}</p><label className="bulk-verification"><input type="checkbox" checked={verified} disabled={bulk.pending || !bulk.ready} onChange={event => setVerifiedIntent(event.target.checked ? intent : null)} /><span>{t("verify", { count: intent.listings.length })}</span></label>
      <div className="catalog-action-row"><Button type="submit" variant={intent.action === "archive" ? "danger" : "primary"} disabled={!verified || !bulk.ready} busy={bulk.pending} busyLabel={t("working")}>{t("apply")}</Button>
        <Button type="button" variant="secondary" disabled={bulk.pending} onClick={cancel}>{t("cancel")}</Button></div>
    </form>}
    {!bulk.ready && <p className="hint">{t("readRequired")}</p>}
  </section>;
}
