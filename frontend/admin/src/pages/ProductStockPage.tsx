import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { api } from "../api";
import { StockAdjustment } from "../components/StockAdjustment";
import { StockMovements } from "../components/StockMovements";
import { variantOptions } from "../components/variantOptions";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { formatNumber } from "../utils/format";
import { useRequest } from "../useRequest";

export function ProductStockPage() {
  const { t, i18n } = useTranslation(["stock"]);
  const { variantId = "" } = useParams();
  const [products, reloadProducts] = useRequest("stock-products", api.products);
  const [stock, reloadStock] = useRequest("stock-detail:" + variantId, api.stock);
  const [movements, reloadMovements] = useRequest(
    "stock-movements:" + variantId,
    (signal) => api.stockMovements(variantId, signal),
  );

  if (products.status === "loading") {
    return <LoadingState label={t("stock:loadingProducts")} lines={5} />;
  }
  if (products.status === "error" || products.status === "not-found") {
    return <RequestError error={products.error} operation="read" onRetry={reloadProducts} />;
  }

  const product = products.data.find((candidate) =>
    candidate.variants.some((variant) => variant.id === variantId));
  const variant = product?.variants.find((candidate) => candidate.id === variantId);
  if (!product || !variant) {
    return (
      <EmptyState title={t("stock:notFoundTitle")} action={<Link to="/stock">{t("stock:backToStock")}</Link>}>
        {t("stock:notFoundBody")}
      </EmptyState>
    );
  }

  const item = stock.status === "ready"
    ? stock.data.find((candidate) => candidate.variantId === variantId)
    : null;
  const current = item ?? { variantId, onHand: 0, reserved: 0, available: 0 };
  const options = variantOptions(product, variant);

  return (
    <div className="stock-detail-page">
      <Link to="/stock" className="physical-back-link">{t("stock:backToStock")}</Link>
      <h1>{t("stock:detailTitle", { sku: variant.sku })}</h1>
      {options && <p className="stock-variant-options">{options}</p>}
      {product.variants.length > 1 && (
        <p className="stock-variant-parent">{t("stock:partOfProduct", { sku: product.sku })}</p>
      )}
      <p className="hint">{t("stock:detailHint")}</p>
      <p><Link to={"/products/" + product.id}>{t("stock:physicalDetail")}</Link></p>
      {products.refreshError !== null && (
        <RequestError error={products.refreshError} operation="read" onRetry={reloadProducts} />
      )}
      <section className="physical-panel stock-summary" aria-labelledby="stock-summary-title">
        <div className="stock-section-heading">
          <h2 id="stock-summary-title">{t("stock:summaryTitle")}</h2>
          <button
            type="button"
            className="link-button"
            data-read-action
            onClick={reloadStock}
            disabled={stock.status === "loading" || (stock.status === "ready" && stock.refreshing)}
          >
            {t("stock:refresh")}
          </button>
        </div>
        {stock.status === "loading" && <LoadingState label={t("stock:loadingStock")} lines={2} />}
        {(stock.status === "error" || stock.status === "not-found") && (
          <RequestError error={stock.error} operation="read" onRetry={reloadStock} />
        )}
        {stock.status === "ready" && (
          <>
            {stock.refreshError !== null && (
              <RequestError error={stock.refreshError} operation="read" onRetry={reloadStock} />
            )}
            {!item && <p className="hint">{t("stock:noRecordHint")}</p>}
            <dl className="stock-quantities">
              <div><dt>{t("stock:onHand")}</dt><dd>{formatNumber(current.onHand, i18n.resolvedLanguage)}</dd></div>
              <div><dt>{t("stock:reserved")}</dt><dd>{formatNumber(current.reserved, i18n.resolvedLanguage)}</dd></div>
              <div><dt>{t("stock:available")}</dt><dd>{formatNumber(current.available, i18n.resolvedLanguage)}</dd></div>
            </dl>
          </>
        )}
      </section>
      {stock.status === "ready" && (
        <StockAdjustment
          key={variantId}
          variantId={variantId}
          current={current}
          onChanged={() => { reloadStock(); reloadMovements(); }}
          onConflict={reloadStock}
        />
      )}
      {movements.status === "loading" && (
        <LoadingState label={t("stock:loadingMovements")} lines={3} />
      )}
      {(movements.status === "error" || movements.status === "not-found") && (
        <section className="physical-panel">
          <h2>{t("stock:movementsTitle")}</h2>
          <RequestError error={movements.error} operation="read" onRetry={reloadMovements} />
        </section>
      )}
      {movements.status === "ready" && (
        <>
          {movements.refreshError !== null && (
            <RequestError error={movements.refreshError} operation="read" onRetry={reloadMovements} />
          )}
          <StockMovements movements={movements.data} />
        </>
      )}
    </div>
  );
}
