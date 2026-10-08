import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { Product, StoreProduct, StoreProductInput } from "../api";
import { generatedCode } from "./attributeValidation";
import { listingDraft, listingInput, validateListing, type ListingDraft, type ListingIssue } from "./listingValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export function ListingForm({ item, products, disabled, pending, onSave, onCancel }: {
  item?: StoreProduct; products?: Product[]; disabled: boolean; pending: boolean;
  onSave: (input: StoreProductInput, productId: string) => Promise<void>; onCancel?: () => void;
}) {
  const { t } = useTranslation("listings");
  const [draft, setDraft] = useState(() => listingDraft(item));
  const [productId, setProductId] = useState("");
  const [productInvalid, setProductInvalid] = useState(false);
  const [issues, setIssues] = useState<ListingIssue[]>([]);
  function update<K extends keyof ListingDraft>(key: K, value: ListingDraft[K]) {
    setDraft((current) => ({ ...current, [key]: value }));
    setIssues((current) => current.filter((issue) => issue.field !== key));
  }
  function error(field: ListingIssue["field"]) { const issue = issues.find((candidate) => candidate.field === field); return issue ? t(`validation.${issue.key}`) : undefined; }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled) return;
    const next = validateListing(draft, !!item); setIssues(next);
    if (!item && !products?.some((candidate) => candidate.id === productId)) {
      setProductInvalid(true); document.getElementById("listing-productId")?.focus(); return;
    }
    if (next.length) { document.getElementById(`listing-${next[0].field}`)?.focus(); return; }
    await onSave(listingInput(draft), item?.productId ?? productId);
  }
  const selected = products?.find((candidate) => candidate.id === productId);
  return <form className="listing-form" noValidate onSubmit={submit}>
    <h2 tabIndex={-1}>{t(item ? "editDetails" : "details")}</h2>
    {(issues.length > 0 || productInvalid) && <div role="alert" className="validation-summary">{t("checkFields")}</div>}
    <fieldset disabled={disabled} className="listing-fields">
      <legend>{t("details")}</legend>
      {!item && <div className="listing-field"><label htmlFor="listing-productId">{t("productToList")}</label>
        <select id="listing-productId" name="productId" value={productId} aria-describedby={`listing-product-hint${productInvalid ? " listing-product-error" : ""}`} aria-invalid={productInvalid || undefined}
          onChange={(event) => { setProductId(event.target.value); setProductInvalid(false); }}>
          <option value="">{t("chooseProduct")}</option>{products?.map((product) => <option key={product.id} value={product.id}>{product.sku} · {t("variantCount", { count: product.variants.length })}</option>)}
        </select><span id="listing-product-hint" className="hint">{t("productChoiceHint")}</span>
        {productInvalid && <span id="listing-product-error" className="field-error">{t("validation.productInvalid")}</span>}
        {selected && <Link to={`/products/${selected.id}`}>{t("physicalProduct", { sku: selected.sku })}</Link>}
      </div>}
      <Field id="listing-name" name="name" label={t("name")} hint={t("nameHint")} value={draft.name} error={error("name")} onChange={(event) => update("name", event.target.value)} />
      <Field id="listing-slug" name="slug" label={t("slug")} hint={t(item ? "slugEditHint" : "slugCreateHint")} value={draft.slug} error={error("slug")} onChange={(event) => update("slug", event.target.value)} />
      {!item && !draft.slug && generatedCode(draft.name) && <p className="hint">{t("slugPreview", { slug: generatedCode(draft.name) })}</p>}
      <div className="listing-field"><label htmlFor="listing-description">{t("description")}</label>
        <textarea id="listing-description" name="description" rows={5} value={draft.description} onChange={(event) => update("description", event.target.value)} />
        <span className="hint">{t("descriptionHint")}</span>
      </div>
      <div className="listing-field-row">
        <Field id="listing-price" name="price" label={t("price")} hint={t("priceHint")} inputMode="decimal" value={draft.price} error={error("price")} onChange={(event) => update("price", event.target.value)} />
        <Field id="listing-vatRate" name="vatRate" label={t("vatRate")} inputMode="decimal" value={draft.vatRate} error={error("vatRate")} onChange={(event) => update("vatRate", event.target.value)} />
      </div>
      <Field id="listing-sortOrder" name="sortOrder" label={t("sortOrder")} hint={t("sortHint")} value={draft.sortOrder} error={error("sortOrder")} onChange={(event) => update("sortOrder", event.target.value)} />
      <label className="listing-check"><input type="checkbox" name="isVisible" checked={draft.isVisible} onChange={(event) => update("isVisible", event.target.checked)} />{t("storefrontVisible")}</label>
    </fieldset>
    <div className="cluster"><Button type="submit" disabled={disabled} busy={pending} busyLabel={t("saving")}>{t(item ? "saveDetails" : "listProduct")}</Button>
      {onCancel && <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button>}
    </div>
  </form>;
}
