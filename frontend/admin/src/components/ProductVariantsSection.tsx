import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api, type Product, type ProductVariant } from "../api";
import { useAction } from "../useAction";
import { ProductOptionsForm, VariantForm } from "./ProductVariantForms";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";
import { productVariantRevision } from "./variantValidation";
import { variantOptions } from "./variantOptions";

type Editor = { kind: "options" | "variant" | "brand" | "delete"; product: Product; variant?: ProductVariant };

export function ProductVariantsSection({ product, canManage, refreshing, refreshFailed, reloadProducts }: {
  product: Product; canManage: boolean; refreshing: boolean; refreshFailed: boolean; reloadProducts: () => void;
}) {
  const { t } = useTranslation("products");
  const [editor, setEditor] = useState<Editor | null>(null);
  const [saved, setSaved] = useState(false);
  const [attempted, setAttempted] = useState(false);
  const [error, run, pending] = useAction(reloadProducts);
  const heading = useRef<HTMLHeadingElement>(null);
  const editorRoot = useRef<HTMLDivElement>(null);
  const opener = useRef<HTMLButtonElement | null>(null);
  const stale = editor !== null && productVariantRevision(editor.product) !== productVariantRevision(product);
  const disabled = !canManage || pending || refreshing || refreshFailed || stale;

  useEffect(() => {
    if (editor) {
      const first = editorRoot.current?.querySelector<HTMLElement>("input")
        ?? editorRoot.current?.querySelector<HTMLElement>("h3");
      first?.focus();
    }
  }, [editor]);

  function open(kind: Editor["kind"], button: HTMLButtonElement, variant?: ProductVariant) {
    if (disabled || editor) return;
    opener.current = button;
    setSaved(false); setAttempted(false);
    setEditor({ kind, product, variant });
  }
  function close() {
    setEditor(null); setAttempted(false);
    window.requestAnimationFrame(() => opener.current?.focus());
  }
  async function save(change: () => Promise<unknown>) {
    if (disabled) return;
    setAttempted(true); setSaved(false);
    await run(async () => {
      await change();
      setEditor(null); setSaved(true);
      window.requestAnimationFrame(() => heading.current?.focus());
    });
  }
  const variants = [...product.variants].sort((left, right) => left.position - right.position);
  return (
    <section className="physical-panel product-variants" aria-labelledby="product-variants-title">
      <h2 id="product-variants-title" ref={heading} tabIndex={-1}>{t("variantsTitle")}</h2>
      <p className="hint">{t("variantsHint")}</p>
      <div className="variant-product-context">
        <div><strong>{t("brand")}</strong><p>{product.brand || "—"}</p></div>
        <div><strong>{t("optionAxes")}</strong><p>{product.optionNames.join(" · ") || t("noOptions")}</p></div>
        {canManage && <div className="cluster">
          <Button type="button" variant="secondary" disabled={disabled || !!editor} onClick={(event) => open("brand", event.currentTarget)}>{t("editBrand")}</Button>
          <Button type="button" variant="secondary" disabled={disabled || !!editor} onClick={(event) => open("options", event.currentTarget)}>{t("editOptions")}</Button>
          <Button type="button" disabled={disabled || !!editor} onClick={(event) => open("variant", event.currentTarget)}>{t("addVariant")}</Button>
        </div>}
      </div>
      {saved && <InlineMessage tone="success">{t("variantsSaved")}</InlineMessage>}
      {(refreshing || refreshFailed) && <InlineMessage>{t(refreshFailed ? "variantRefreshFailed" : "variantRefreshing")}</InlineMessage>}
      {stale && <InlineMessage>{t("variantDraftStale")}</InlineMessage>}
      <ul className="variant-list">
        {variants.map((variant, index) => <li key={variant.id} className="variant-card">
          <div className="variant-card-heading"><h3>{variant.sku}</h3>{index === 0 && <span className="variant-default">{t("defaultVariant")}</span>}</div>
          {variantOptions(product, variant) && <p className="stock-variant-options">{variantOptions(product, variant)}</p>}
          <dl className="variant-facts">
            <div><dt>{t("ean")}</dt><dd>{variant.ean || "—"}</dd></div>
            <div><dt>{t("weight")}</dt><dd>{variant.weightGrams === null ? "—" : t("grams", { count: variant.weightGrams })}</dd></div>
            <div><dt>{t("partNumber")}</dt><dd>{variant.partNumber || "—"}</dd></div>
            <div><dt>{t("condition")}</dt><dd>{conditionsLabel(variant.condition)}</dd></div>
          </dl>
          <div className="variant-card-actions">
            <Link to={`/stock/${variant.id}`}><strong>{variant.sku}</strong> {t("openStock")}</Link>
            {canManage && <div className="cluster">
              <Button type="button" variant="secondary" disabled={disabled || !!editor} onClick={(event) => open("variant", event.currentTarget, variant)}>{t("editVariant", { sku: variant.sku })}</Button>
              <Button type="button" variant="quiet" disabled={disabled || !!editor || variants.length === 1} onClick={(event) => open("delete", event.currentTarget, variant)}>{t("removeVariant", { sku: variant.sku })}</Button>
            </div>}
          </div>
        </li>)}
      </ul>
      <p className="hint">{t("defaultVariantHint")}</p>
      {variants.length === 1 && <p className="hint">{t("lastVariantHint")}</p>}
      {editor && <div ref={editorRoot}>
        {editor.kind === "variant" && <VariantForm product={editor.product} variant={editor.variant} disabled={disabled} pending={pending} onCancel={close}
          onSave={(input) => save(() => editor.variant
            ? api.updateVariant(product.id, editor.variant.id, input) : api.addVariant(product.id, input))} />}
        {editor.kind === "options" && <ProductOptionsForm product={editor.product} disabled={disabled} pending={pending} onCancel={close}
          onSave={(input) => save(() => api.setProductOptions(product.id, input))} />}
        {editor.kind === "brand" && <BrandForm product={editor.product} disabled={disabled} pending={pending} onCancel={close}
          onSave={(brand) => save(() => {
            const only = editor.product.variants.length === 1 ? editor.product.variants[0] : null;
            // The compatibility endpoint replaces physical fields for a single variant, even for a brand edit.
            return api.updateProduct(product.id, { brand, ean: only?.ean ?? null, weightGrams: only?.weightGrams ?? null,
              partNumber: only?.partNumber ?? null, condition: only?.condition ?? null });
          })} />}
        {editor.kind === "delete" && editor.variant && <div className="variant-editor" role="group" aria-labelledby="variant-delete-title">
          <h3 id="variant-delete-title" tabIndex={-1}>{t("confirmVariantDelete", { sku: editor.variant.sku })}</h3>
          <p>{t("variantDeleteHint")}</p>
          {editor.variant.id === variants[0]?.id && <p>{t("deleteDefaultHint")}</p>}
          <div className="cluster">
            <Button type="button" variant="danger" disabled={disabled} busy={pending} busyLabel={t("removingVariant")}
              onClick={() => void save(() => api.deleteVariant(product.id, editor.variant!.id))}>{t("confirmVariantRemove")}</Button>
            <Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancel")}</Button>
          </div>
        </div>}
        {attempted && error !== null && <RequestError error={error} operation="write" />}
      </div>}
    </section>
  );
  function conditionsLabel(condition: string | null) {
    switch (condition) {
      case "New": return t("conditionNew");
      case "Refurbished": return t("conditionRefurbished");
      case "Used": return t("conditionUsed");
      default: return t("conditionUnstated");
    }
  }
}

function BrandForm({ product, disabled, pending, onSave, onCancel }: {
  product: Product; disabled: boolean; pending: boolean; onSave: (brand: string | null) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation("products");
  const [brand, setBrand] = useState(product.brand ?? "");
  const [invalid, setInvalid] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled) return;
    if (brand.trim().length > 70) { setInvalid(true); return; }
    await onSave(brand.trim() || null);
  }
  return <form className="variant-editor" noValidate onSubmit={submit}>
    <h3 tabIndex={-1}>{t("editBrand")}</h3>
    <Field name="brand" label={t("brand")} hint={t("brandHint")} value={brand} disabled={disabled}
      error={invalid && t("validation.brandInvalid")} onChange={(event) => { setBrand(event.target.value); setInvalid(false); }} />
    <div className="cluster">
      <Button type="submit" disabled={disabled} busy={pending} busyLabel={t("savingProduct")}>{t("saveBrand")}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button>
    </div>
  </form>;
}
