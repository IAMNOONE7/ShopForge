import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { api, type Product } from "../api";
import { PermissionScope } from "../components/PermissionScope";
import { ProductIssueSummary } from "../components/ProductIssueSummary";
import { ProductMediaSection } from "../components/ProductMediaSection";
import { variantOptions } from "../components/variantOptions";
import {
  conditions,
  physicalInput,
  validateProduct,
  type ProductDraft,
  type ProductIssue,
} from "../components/productValidation";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { Field } from "../components/ui/Field";
import { InlineMessage } from "../components/ui/InlineMessage";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";

export function PhysicalProductPage() {
  const { t } = useTranslation(["products"]);
  const { productId = "" } = useParams();
  const { user } = useSession();
  const [products, reloadProducts] = useRequest(
    "physical-products",
    api.products,
  );

  if (products.status === "loading") {
    return <LoadingState label={t("products:loading")} lines={6} />;
  }
  if (products.status === "error" || products.status === "not-found") {
    return (
      <RequestError
        error={products.error}
        operation="read"
        onRetry={reloadProducts}
      />
    );
  }

  const product = products.data.find((candidate) => candidate.id === productId);
  if (!product) {
    return (
      <EmptyState
        title={t("products:notFoundTitle")}
        action={<Link to="/products">{t("products:backToProducts")}</Link>}
      >
        {t("products:notFoundBody")}
      </EmptyState>
    );
  }

  return (
    <div className="physical-detail-page">
      <Link to="/products" className="physical-back-link">
        {t("products:backToProducts")}
      </Link>
      <h1>{t("products:detailTitle", { sku: product.sku })}</h1>
      <p className="hint">{t("products:detailHint")}</p>
      {products.refreshError !== null && (
        <RequestError
          error={products.refreshError}
          operation="read"
          onRetry={reloadProducts}
        />
      )}
      <PermissionScope allowed={canManageCatalog(user.role)}>
        <div className="physical-detail-panels">
          <PhysicalDataSection
            key={`physical:${product.id}`}
            product={product}
            reloadProducts={reloadProducts}
          />
          <ProductMediaSection
            key={`media:${product.id}`}
            productId={product.id}
            sku={product.sku}
            images={product.images}
            reloadProducts={reloadProducts}
          />
          <section className="physical-panel" aria-labelledby="product-stock-title">
            <h2 id="product-stock-title">{t("stock")}</h2>
            <p className="hint">{t("stockHint")}</p>
            <ul className="physical-stock-links">
              {product.variants.map((variant) => (
                <li key={variant.id}>
                  <Link to={`/stock/${variant.id}`}>
                    <strong>{variant.sku}</strong>{" "}
                    <span>{t("openStock")}</span>
                  </Link>
                  {variantOptions(product, variant) && (
                    <p className="stock-variant-options">{variantOptions(product, variant)}</p>
                  )}
                </li>
              ))}
            </ul>
          </section>
        </div>
      </PermissionScope>
    </div>
  );
}

function PhysicalDataSection({
  product,
  reloadProducts,
}: {
  product: Product;
  reloadProducts: () => void;
}) {
  const { t } = useTranslation("products");
  const [draft, setDraft] = useState<ProductDraft>({
    sku: product.sku,
    ean: product.ean ?? "",
    weightGrams: product.weightGrams?.toString() ?? "",
    brand: product.brand ?? "",
    partNumber: product.variants[0]?.partNumber ?? "",
    condition: product.variants[0]?.condition ?? "",
  });
  const [issues, setIssues] = useState<ProductIssue[]>([]);
  const [saved, setSaved] = useState(false);
  const [error, run, pending] = useAction(reloadProducts);

  function update(field: "ean" | "weightGrams" | "brand" | "partNumber" | "condition", value: string) {
    setSaved(false);
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) => current.filter((issue) => issue.field !== field));
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaved(false);
    const nextIssues = validateProduct(draft, false);
    setIssues(nextIssues);
    if (nextIssues.length) {
      window.requestAnimationFrame(() =>
        document.getElementById("physical-data-validation")?.focus(),
      );
      return;
    }
    await run(async () => {
      const updated = await api.updateProduct(product.id, physicalInput(draft));
      setDraft({
        sku: updated.sku,
        ean: updated.ean ?? "",
        weightGrams: updated.weightGrams?.toString() ?? "",
        brand: updated.brand ?? "",
        partNumber: updated.variants[0]?.partNumber ?? "",
        condition: updated.variants[0]?.condition ?? "",
      });
      setSaved(true);
    });
  }

  if (product.variants.length > 1) {
    return (
      <section className="physical-panel" aria-labelledby="physical-data-title">
        <h2 id="physical-data-title">{t("physicalDetails")}</h2>
        <InlineMessage>{t("multiVariantPhysicalHint")}</InlineMessage>
      </section>
    );
  }

  return (
    <section className="physical-panel" aria-labelledby="physical-data-title">
      <h2 id="physical-data-title">{t("physicalDetails")}</h2>
      <p className="hint">{t("physicalDetailsHint")}</p>
      <form className="physical-form" noValidate onSubmit={save}>
        <ProductIssueSummary id="physical-data-validation" issues={issues} />
        <Field
          id="sku"
          name="sku"
          label={t("sku")}
          value={product.sku}
          readOnly
          aria-readonly="true"
          hint={t("skuReadOnlyHint")}
        />
        <div className="physical-fields-row">
          <Field
            id="ean"
            name="ean"
            label={t("eanOptional")}
            hint={t("eanHint")}
            inputMode="numeric"
            value={draft.ean}
            aria-invalid={issues.some((issue) => issue.field === "ean") || undefined}
            onChange={(event) => update("ean", event.target.value)}
          />
          <Field
            id="weightGrams"
            name="weightGrams"
            label={t("weightGrams")}
            hint={t("weightHint")}
            inputMode="numeric"
            value={draft.weightGrams}
            aria-invalid={issues.some((issue) => issue.field === "weightGrams") || undefined}
            onChange={(event) => update("weightGrams", event.target.value)}
          />
        </div>
        {/* What a shopping feed asks for. A brand is the product's; the rest belong to this form of it. */}
        <div className="physical-fields-row">
          <Field
            id="brand"
            name="brand"
            label={t("brand")}
            hint={t("brandHint")}
            value={draft.brand}
            aria-invalid={issues.some((issue) => issue.field === "brand") || undefined}
            onChange={(event) => update("brand", event.target.value)}
          />
          <Field
            id="partNumber"
            name="partNumber"
            label={t("partNumber")}
            hint={t("partNumberHint")}
            value={draft.partNumber}
            aria-invalid={issues.some((issue) => issue.field === "partNumber") || undefined}
            onChange={(event) => update("partNumber", event.target.value)}
          />
          <label htmlFor="condition">
            {t("condition")}
            <select
              id="condition"
              name="condition"
              value={draft.condition}
              onChange={(event) => update("condition", event.target.value)}
            >
              {conditions.map((condition) => (
                <option key={condition} value={condition}>
                  {condition === "" ? t("conditionUnstated") : t(`condition${condition}`)}
                </option>
              ))}
            </select>
          </label>
        </div>
        <Button
          type="submit"
          busy={pending}
          busyLabel={t("savingProduct")}
        >
          {t("savePhysicalDetails")}
        </Button>
        {saved && (
          <InlineMessage tone="success">{t("productSaved")}</InlineMessage>
        )}
        {error !== null && <RequestError error={error} operation="write" />}
      </form>
    </section>
  );
}

