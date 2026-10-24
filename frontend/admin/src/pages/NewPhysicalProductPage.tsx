import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { api } from "../api";
import { ProductIssueSummary } from "../components/ProductIssueSummary";
import { PackageFields } from "../components/PackageFields";
import {
  physicalInput,
  validateProduct,
  type ProductDraft,
  type ProductIssue,
} from "../components/productValidation";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { RequestError } from "../components/ui/RequestError";
import { useAction } from "../useAction";

export function NewPhysicalProductPage() {
  const { t } = useTranslation("products");
  const navigate = useNavigate();
  const [draft, setDraft] = useState<ProductDraft>({
    sku: "",
    ean: "",
    weightGrams: "",
    lengthMm: "",
    widthMm: "",
    heightMm: "",
    shipsAlone: false,
    brand: "",
    partNumber: "",
    condition: "",
  });
  const [issues, setIssues] = useState<ProductIssue[]>([]);
  const [error, run, pending] = useAction(() => undefined);

  function update<K extends keyof ProductDraft>(field: K, value: ProductDraft[K]) {
    setDraft((current) => ({ ...current, [field]: value }));
    setIssues((current) =>
      current.filter((issue) => issue.field !== field),
    );
  }

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending) return;
    const nextIssues = validateProduct(draft, true);
    setIssues(nextIssues);
    if (nextIssues.length) {
      window.requestAnimationFrame(() =>
        document.getElementById("new-product-validation")?.focus(),
      );
      return;
    }
    await run(async () => {
      const product = await api.createProduct({
        sku: draft.sku.trim(),
        ...physicalInput(draft),
      });
      void navigate(`/products/${product.id}`);
    });
  }

  return (
    <div className="physical-detail-page">
      <Link to="/products" className="physical-back-link">
        {t("backToProducts")}
      </Link>
      <h1>{t("newProduct")}</h1>
      <p className="hint">{t("newProductHint")}</p>
      <form className="physical-panel physical-form" noValidate onSubmit={create}>
        <ProductIssueSummary id="new-product-validation" issues={issues} />
        <Field
          id="sku"
          name="sku"
          label={t("sku")}
          hint={t("skuHint")}
          maxLength={64}
          autoFocus
          value={draft.sku}
          aria-invalid={issues.some((issue) => issue.field === "sku") || undefined}
          onChange={(event) => update("sku", event.target.value)}
        />
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
        <PackageFields draft={draft} issues={issues} onChange={update} />
        <Button
          type="submit"
          busy={pending}
          busyLabel={t("creatingProduct")}
        >
          {t("addProduct")}
        </Button>
        {error !== null && <RequestError error={error} operation="write" />}
      </form>
    </div>
  );
}
