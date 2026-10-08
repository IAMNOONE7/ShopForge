import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { api } from "../api";
import { PermissionScope } from "../components/PermissionScope";
import { ProductMediaSection } from "../components/ProductMediaSection";
import { ProductVariantsSection } from "../components/ProductVariantsSection";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
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
          <ProductVariantsSection
            key={product.id}
            product={product}
            canManage={canManageCatalog(user.role)}
            refreshing={products.refreshing}
            refreshFailed={products.refreshError !== null}
            reloadProducts={reloadProducts}
          />
          <ProductMediaSection
            key={`media:${product.id}`}
            productId={product.id}
            sku={product.sku}
            images={product.images}
            reloadProducts={reloadProducts}
          />

        </div>
      </PermissionScope>
    </div>
  );
}
