import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api } from "../api";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
import { useRequest } from "../useRequest";

export function ProductsPage() {
  const { t } = useTranslation(["products", "common"]);
  const { user } = useSession();
  const [products, reloadProducts] = useRequest("physical-products", api.products);
  const canManage = canManageCatalog(user.role);

  return (
    <div className="physical-products-page">
      <div className="page-heading-row">
        <div>
          <h1>{t("products:title")}</h1>
          <p className="hint">{t("products:sharedHint")}</p>
        </div>
        {canManage && (
          <Link to="/products/new" className="button">
            {t("products:newProduct")}
          </Link>
        )}
      </div>

      {(products.status === "error" || products.status === "not-found") && (
        <RequestError
          error={products.error}
          operation="read"
          onRetry={reloadProducts}
        />
      )}
      {products.status === "loading" && (
        <LoadingState label={t("products:loading")} lines={5} />
      )}
      {products.status === "ready" && (
        <>
          {products.refreshError !== null && (
            <RequestError
              error={products.refreshError}
              operation="read"
              onRetry={reloadProducts}
            />
          )}
          {products.data.length === 0 ? (
            <EmptyState
              title={t("products:emptyTitle")}
              headingLevel={2}
              action={
                canManage ? (
                  <Link to="/products/new" className="button">
                    {t("products:newProduct")}
                  </Link>
                ) : undefined
              }
            >
              {t("products:emptyBody")}
            </EmptyState>
          ) : (
            <>
              <p className="hint">
                {t("products:count", { count: products.data.length })}
              </p>
              <table className="physical-products-table">
                <thead>
                  <tr>
                    <th scope="col">{t("products:sku")}</th>
                    <th scope="col">{t("products:ean")}</th>
                    <th scope="col">{t("products:weight")}</th>
                    <th scope="col">{t("products:images")}</th>
                  </tr>
                </thead>
                <tbody>
                  {products.data.map((product) => (
                    <tr key={product.id}>
                      <th scope="row">
                        <Link to={`/products/${product.id}`}>
                          {product.sku}
                        </Link>
                      </th>
                      <td>{product.ean ?? "—"}</td>
                      <td>
                        {product.weightGrams === null
                          ? "—"
                          : t("products:grams", {
                              count: product.weightGrams,
                            })}
                      </td>
                      <td>
                        {t("products:imageCount", {
                          count: product.images.length,
                        })}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <ul className="physical-products-cards">
                {products.data.map((product) => (
                  <li key={product.id}>
                    <Link to={`/products/${product.id}`}>
                      <strong>{product.sku}</strong>
                      <span>{t("products:openProduct")}</span>
                    </Link>
                    <dl>
                      <div>
                        <dt>{t("products:ean")}</dt>
                        <dd>{product.ean ?? "—"}</dd>
                      </div>
                      <div>
                        <dt>{t("products:weight")}</dt>
                        <dd>
                          {product.weightGrams === null
                            ? "—"
                            : t("products:grams", {
                                count: product.weightGrams,
                              })}
                        </dd>
                      </div>
                      <div>
                        <dt>{t("products:images")}</dt>
                        <dd>
                          {t("products:imageCount", {
                            count: product.images.length,
                          })}
                        </dd>
                      </div>
                    </dl>
                  </li>
                ))}
              </ul>
            </>
          )}
        </>
      )}
    </div>
  );
}
