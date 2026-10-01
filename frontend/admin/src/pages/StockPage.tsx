import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api, type Stock } from "../api";
import { variantOptions } from "../components/variantOptions";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { formatNumber } from "../utils/format";
import { canManageCatalog, useSession } from "../session";
import { useRequest } from "../useRequest";

export function StockPage() {
  const { t, i18n } = useTranslation(["stock", "products", "common"]);
  const { user } = useSession();
  const [products, reloadProducts] = useRequest("stock-products", api.products);
  const [stock, reloadStock] = useRequest("stock-list", api.stock);
  const values = stock.status === "ready"
    ? new Map(stock.data.map((item) => [item.variantId, item]))
    : null;
  const variants = products.status === "ready"
    ? products.data.flatMap((product) =>
        product.variants.map((variant) => ({ product, variant })))
    : [];

  function quantity(item: Stock | undefined, field: "onHand" | "reserved" | "available") {
    if (!values) return t(stock.status === "loading" ? "common:loading" : "stock:unavailableValue");
    return formatNumber(item?.[field] ?? 0, i18n.resolvedLanguage);
  }

  return (
    <div className="stock-page">
      <div className="page-heading-row">
        <div>
          <h1>{t("stock:title")}</h1>
          <p className="hint">{t("stock:sharedHint")}</p>
        </div>
        <Link to="/products" className="button">
          {t("stock:physicalProducts")}
        </Link>
      </div>
      {(products.status === "error" || products.status === "not-found") && (
        <RequestError error={products.error} operation="read" onRetry={reloadProducts} />
      )}
      {stock.status === "error" || stock.status === "not-found" ? (
        <RequestError error={stock.error} operation="read" onRetry={reloadStock} />
      ) : stock.status === "ready" && stock.refreshError !== null ? (
        <RequestError error={stock.refreshError} operation="read" onRetry={reloadStock} />
      ) : null}
      {products.status === "loading" && (
        <LoadingState label={t("stock:loadingProducts")} lines={5} />
      )}
      {products.status === "ready" && (
        <>
          {products.refreshError !== null && (
            <RequestError error={products.refreshError} operation="read" onRetry={reloadProducts} />
          )}
          {products.data.length === 0 ? (
            <EmptyState
              title={t("stock:noProductsTitle")}
              headingLevel={2}
              action={canManageCatalog(user.role) ? <Link to="/products/new">{t("stock:createProduct")}</Link> : undefined}
            >
              {t("stock:noProductsBody")}
            </EmptyState>
          ) : (
            <>
              {stock.status === "loading" && (
                <p className="hint" role="status">{t("stock:loadingStock")}</p>
              )}
              <p className="hint">{t("stock:entryCount", { count: variants.length })}</p>
              <table className="stock-table">
                <thead><tr>
                  <th scope="col">{t("stock:variant")}</th>
                  <th scope="col">{t("stock:onHand")}</th>
                  <th scope="col">{t("stock:reserved")}</th>
                  <th scope="col">{t("stock:available")}</th>
                  <th scope="col">{t("stock:record")}</th>
                </tr></thead>
                <tbody>
                  {variants.map(({ product, variant }) => {
                    const item = values?.get(variant.id);
                    const options = variantOptions(product, variant);
                    return (
                      <tr key={variant.id}>
                        <th scope="row">
                          <Link to={"/stock/" + variant.id}>{variant.sku}</Link>{" "}
                          {options && <span className="stock-variant-options">{options}</span>}{" "}
                          {product.variants.length > 1 && (
                            <span className="stock-variant-parent">{t("stock:partOfProduct", { sku: product.sku })}</span>
                          )}
                        </th>
                        <td>{quantity(item, "onHand")}</td>
                        <td>{quantity(item, "reserved")}</td>
                        <td>{quantity(item, "available")}</td>
                        <td>{values ? t(item ? "stock:recorded" : "stock:notRecorded") : "—"}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
              <ul className="stock-cards">
                {variants.map(({ product, variant }) => {
                  const item = values?.get(variant.id);
                  const options = variantOptions(product, variant);
                  return (
                    <li key={variant.id}>
                      <Link to={"/stock/" + variant.id} className="stock-card-link">
                        <strong>{variant.sku}</strong><span>{t("stock:openDetail")}</span>
                      </Link>
                      {options && <p className="stock-variant-options">{options}</p>}
                      {product.variants.length > 1 && (
                        <p className="stock-variant-parent">{t("stock:partOfProduct", { sku: product.sku })}</p>
                      )}
                      <dl>
                        <div><dt>{t("stock:onHand")}</dt><dd>{quantity(item, "onHand")}</dd></div>
                        <div><dt>{t("stock:reserved")}</dt><dd>{quantity(item, "reserved")}</dd></div>
                        <div><dt>{t("stock:available")}</dt><dd>{quantity(item, "available")}</dd></div>
                      </dl>
                      {values && !item && <p className="hint">{t("stock:notRecorded")}</p>}
                    </li>
                  );
                })}
              </ul>
            </>
          )}
        </>
      )}
    </div>
  );
}
