import { useTranslation } from "react-i18next";
import { api, type Stock } from "../api";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { RequestError } from "../components/ui/RequestError";
import { PermissionScope } from "../components/PermissionScope";
import { canManageCatalog, useSession } from "../session";

export function ProductsPage() {
  const { t } = useTranslation(["products", "stock", "common", "errors"]);
  const { user } = useSession();
  const canManage = canManageCatalog(user.role);
  const [products, reloadProducts] = useRequest("products", api.products);
  const [stock, reloadStock] = useRequest("stock", api.stock);
  const [error, run] = useAction(() => {
    reloadProducts();
    reloadStock();
  });
  const stockLevels: Stock[] = stock.status === "ready" ? stock.data : [];
  async function createProduct(form: FormData) {
    const weight = String(form.get("weightGrams"));
    await run(() =>
      api.createProduct({
        sku: String(form.get("sku")),
        ean: String(form.get("ean")) || null,
        weightGrams: weight ? Number(weight) : null,
      }),
    );
  }
  return (
    <>
      <h1>{t("products:title")}</h1>
      <p className="hint">{t("products:sharedHint")}</p>
      <PermissionScope allowed={canManage}>
        <form action={createProduct} className="inline-form">
          <input name="sku" placeholder={t("products:sku")} required />
          <input
            name="ean"
            placeholder={t("products:eanOptional")}
            inputMode="numeric"
          />
          <input
            name="weightGrams"
            placeholder={t("products:weightGrams")}
            type="number"
            min="0"
          />
          <button type="submit">{t("products:addProduct")}</button>
        </form>
        {error !== null && <RequestError error={error} operation="write" />}
        {products.status === "error" && (
          <RequestError
            error={products.error}
            operation="read"
            onRetry={reloadProducts}
          />
        )}
        {stock.status === "error" && (
          <RequestError
            error={stock.error}
            operation="read"
            onRetry={reloadStock}
          />
        )}
        {products.status === "loading" && (
          <LoadingState label={t("products:loading")} lines={5} />
        )}{" "}
        {products.status === "ready" && products.data.length === 0 && (
          <EmptyState title={t("products:emptyTitle")} headingLevel={2}>
            {t("products:emptyBody")}
          </EmptyState>
        )}
        {products.status === "ready" && products.data.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>{t("products:sku")}</th>
                <th>{t("products:ean")}</th>
                <th>{t("products:weight")}</th>
                <th>{t("products:stock")}</th>
                <th>{t("products:images")}</th>
              </tr>
            </thead>
            <tbody>
              {products.data.map((product) => (
                <tr key={product.id}>
                  <td>{product.sku}</td>
                  <td>{product.ean ?? "—"}</td>
                  <td>
                    {product.weightGrams === null
                      ? "—"
                      : `${product.weightGrams} g`}
                  </td>
                  <td>
                    <form
                      className="inline-form compact"
                      action={(form) =>
                        run(() =>
                          api.setStock(
                            product.id,
                            Number(form.get("quantity")),
                          ),
                        )
                      }
                      key={stockOf(stockLevels, product.id).onHand}
                    >
                      <input
                        name="quantity"
                        type="number"
                        min="0"
                        defaultValue={stockOf(stockLevels, product.id).onHand}
                        aria-label={t("stock:onHand")}
                      />
                      <button type="submit">{t("common:save")}</button>
                      {stockOf(stockLevels, product.id).reserved > 0 && (
                        <span className="hint">
                          {t("stock:reserved", {
                            count: stockOf(stockLevels, product.id).reserved,
                          })}
                        </span>
                      )}
                    </form>
                  </td>
                  <td>
                    <div className="thumbnails">
                      {product.images.map((image) => (
                        <span key={image.id} className="thumbnail">
                          <img
                            src={image.url}
                            alt={image.altText ?? product.sku}
                          />
                          <button
                            type="button"
                            aria-label={t("products:removeImage")}
                            onClick={() =>
                              run(() =>
                                api.deleteProductImage(product.id, image.id),
                              )
                            }
                          >
                            ×
                          </button>
                        </span>
                      ))}
                      <label className="upload">
                        {t("products:addImage")}
                        <input
                          type="file"
                          accept="image/jpeg,image/png,image/webp"
                          onChange={(event) => {
                            const file = event.target.files?.[0];
                            event.target.value = "";
                            if (file)
                              void run(() =>
                                api.uploadProductImage(
                                  product.id,
                                  file,
                                  product.sku,
                                ),
                              );
                          }}
                        />
                      </label>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </PermissionScope>
    </>
  );
}
function stockOf(levels: Stock[], productId: string): Stock {
  return (
    levels.find((level) => level.productId === productId) ?? {
      productId,
      onHand: 0,
      reserved: 0,
      available: 0,
    }
  );
}
