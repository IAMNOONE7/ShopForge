import { useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import {
  api,
  type AttributeDefinition,
  type AttributeValues,
  type Category,
  type StoreProduct,
  type StoreProductInput,
} from "../api";
import { useSelectedStore } from "../adminContext";
import { AttributeValueFields } from "../components/AttributeValueFields";
import { DiscountsSection } from "../components/DiscountsSection";
import { FailedMessagesSection } from "../components/FailedMessagesSection";
import { ImportSection } from "../components/ImportSection";
import { MethodsSection } from "../components/MethodsSection";
import { OrdersSection } from "../components/OrdersSection";
import { PermissionScope } from "../components/PermissionScope";
import { ReturnsSection } from "../components/ReturnsSection";
import { ReviewsSection } from "../components/ReviewsSection";
import { StoreLogoSection } from "../components/StoreLogoSection";
import { StorePublicationSection } from "../components/StorePublicationSection";
import { StoreSettingsSection } from "../components/StoreSettingsSection";
import { readAttributeValues } from "../components/attributeValues";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useRequest, type RequestState } from "../useRequest";
import { currencyFormatter } from "../utils/format";

function StoreRoute({
  title,
  allowed,
  children,
}: {
  title: string;
  allowed: boolean;
  children: ReactNode;
}) {
  return (
    <div className="admin-route-section">
      <h1>{title}</h1>
      <PermissionScope allowed={allowed}>{children}</PermissionScope>
    </div>
  );
}

export function StoreSettingsPage() {
  const { t } = useTranslation("stores");
  const { user } = useSession();
  const { store, reloadStores } = useSelectedStore();
  return (
    <StoreRoute title={t("settings")} allowed={canManageStore(user.role)}>
      <div className="store-settings-workspace">
        <StorePublicationSection
          store={store}
          reloadStores={reloadStores}
        />
        <StoreSettingsSection
          store={store}
          reloadStores={reloadStores}
        />
        <StoreLogoSection
          key={store.id}
          store={store}
          reloadStores={reloadStores}
        />
      </div>
    </StoreRoute>
  );
}

export function StoreMethodsPage() {
  const { t, i18n } = useTranslation("methods");
  const { user } = useSession();
  const { store } = useSelectedStore();
  const [, run] = useAction(() => undefined);
  return (
    <StoreRoute title={t("title")} allowed={canManageStore(user.role)}>
      <MethodsSection
        storeId={store.id}
        money={currencyFormatter(store.currency, i18n.resolvedLanguage)}
        run={run}
      />
    </StoreRoute>
  );
}

export function StoreDiscountsPage() {
  const { t, i18n } = useTranslation("discounts");
  const { user } = useSession();
  const { store } = useSelectedStore();
  return (
    <StoreRoute title={t("title")} allowed={canManageStore(user.role)}>
      <DiscountsSection
        storeId={store.id}
        money={currencyFormatter(store.currency, i18n.resolvedLanguage)}
      />
    </StoreRoute>
  );
}

export function StoreImportPage() {
  const { t } = useTranslation(["import", "errors"]);
  const { user } = useSession();
  const { store } = useSelectedStore();
  const [error, run] = useAction(() => undefined);
  return (
    <StoreRoute title={t("import:title")} allowed={canManageCatalog(user.role)}>
      {error !== null && <RequestError error={error} operation="write" />}
      <ImportSection storeId={store.id} run={run} />
    </StoreRoute>
  );
}

export function StoreCategoriesPage() {
  const { t } = useTranslation(["categories", "attributes", "common"]);
  const { user } = useSession();
  const { store } = useSelectedStore();
  const [categories, reloadCategories] = useRequest(
    `categories:${store.id}`,
    (signal) => api.categories(store.id, signal),
  );
  const [attributes, reloadAttributes] = useRequest(
    `attributes:${store.id}`,
    (signal) => api.attributes(store.id, signal),
  );
  const [error, run] = useAction(() => {
    reloadCategories();
    reloadAttributes();
  });
  const categoryList = categories.status === "ready" ? categories.data : [];
  const attributeList = attributes.status === "ready" ? attributes.data : [];
  const loading =
    categories.status === "loading" || attributes.status === "loading";
  return (
    <StoreRoute
      title={t("categories:title")}
      allowed={canManageCatalog(user.role)}
    >
      {requestError(categories, reloadCategories)}
      {requestError(attributes, reloadAttributes)}
      {error !== null && <RequestError error={error} operation="write" />}
      {loading && <LoadingState label={t("categories:loading")} lines={4} />}
      {!loading &&
        categories.status === "ready" &&
        attributes.status === "ready" && (
          <section>
            <h2>{t("categories:title")}</h2>
            <p className="hint">{t("categories:hint")}</p>
            {categoryList.map((category) => (
              <form
                key={category.id}
                className="inline-form"
                action={(form) =>
                  run(() =>
                    api.assignCategoryAttributes(
                      store.id,
                      category.id,
                      form.getAll("attributeIds").map(String),
                    ),
                  )
                }
              >
                <strong className="chip">{category.name}</strong>
                {attributeList.map((attribute) => (
                  <label key={attribute.id}>
                    <input
                      name="attributeIds"
                      type="checkbox"
                      value={attribute.id}
                      defaultChecked={category.attributeIds.includes(
                        attribute.id,
                      )}
                    />{" "}
                    {attribute.name}
                  </label>
                ))}
                <button type="submit">{t("common:save")}</button>
              </form>
            ))}
            <form
              action={(form) =>
                run(() =>
                  api.createCategory(store.id, String(form.get("name"))),
                )
              }
              className="inline-form"
            >
              <input
                name="name"
                placeholder={t("categories:newCategory")}
                required
              />
              <button type="submit">{t("categories:addCategory")}</button>
            </form>
          </section>
        )}
    </StoreRoute>
  );
}

export function StoreProductsPage() {
  const { t, i18n } = useTranslation([
    "listings",
    "products",
    "categories",
    "attributes",
    "common",
  ]);
  const { user } = useSession();
  const { store } = useSelectedStore();
  const [storeProducts, reloadStoreProducts] = useRequest(
    `store-products:${store.id}`,
    (signal) => api.storeProducts(store.id, signal),
  );
  const [categories, reloadCategories] = useRequest(
    `categories:${store.id}`,
    (signal) => api.categories(store.id, signal),
  );
  const [attributes, reloadAttributes] = useRequest(
    `attributes:${store.id}`,
    (signal) => api.attributes(store.id, signal),
  );
  const [products, reloadProducts] = useRequest("products", api.products);
  const [error, run] = useAction(() => {
    reloadStoreProducts();
    reloadCategories();
    reloadAttributes();
    reloadProducts();
  });
  const [editing, setEditing] = useState<string | null>(null);
  const states = [storeProducts, categories, attributes, products];
  const loading = states.some((state) => state.status === "loading");
  const categoryList = categories.status === "ready" ? categories.data : [];
  const attributeList = attributes.status === "ready" ? attributes.data : [];
  const listed = storeProducts.status === "ready" ? storeProducts.data : [];
  const unlisted =
    products.status === "ready"
      ? products.data.filter(
          (product) => !listed.some((item) => item.productId === product.id),
        )
      : [];
  const money = currencyFormatter(store.currency, i18n.resolvedLanguage);

  async function listProduct(form: FormData) {
    await run(() =>
      api.listProduct(store.id, String(form.get("productId")), {
        name: String(form.get("name")),
        price: Number(form.get("price")),
        vatRate: Number(form.get("vatRate")),
        isVisible: form.get("isVisible") === "on",
        sortOrder: 0,
      }),
    );
  }

  return (
    <StoreRoute
      title={t("listings:title")}
      allowed={canManageCatalog(user.role)}
    >
      {requestError(storeProducts, reloadStoreProducts)}
      {requestError(categories, reloadCategories)}
      {requestError(attributes, reloadAttributes)}
      {requestError(products, reloadProducts)}
      {error !== null && <RequestError error={error} operation="write" />}
      {loading && <LoadingState label={t("listings:loading")} lines={5} />}
      {!loading && states.every((state) => state.status === "ready") && (
        <section>
          <h2>{t("listings:title")}</h2>
          <form action={listProduct} className="inline-form">
            <select name="productId" required defaultValue="">
              <option value="" disabled>
                {t("listings:productToList")}
              </option>
              {unlisted.map((product) => (
                <option key={product.id} value={product.id}>
                  {product.sku}
                </option>
              ))}
            </select>
            <input name="name" placeholder={t("listings:storeName")} required />
            <input
              name="price"
              placeholder={t("listings:price")}
              type="number"
              min="0"
              step="0.01"
              required
            />
            <input
              name="vatRate"
              placeholder={t("listings:vatPercent")}
              type="number"
              min="0"
              max="100"
              step="0.01"
              defaultValue="21"
              required
            />
            <label>
              <input name="isVisible" type="checkbox" defaultChecked />{" "}
              {t("common:visible")}
            </label>
            <button type="submit">{t("listings:listProduct")}</button>
          </form>
          <table>
            <thead>
              <tr>
                <th>{t("listings:name")}</th>
                <th>{t("products:sku")}</th>
                <th>{t("listings:price")}</th>
                <th>{t("listings:vat")}</th>
                <th>{t("common:visible")}</th>
                <th>{t("listings:categories")}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {listed.map((item) =>
                editing === item.id ? (
                  <EditRow
                    key={item.id}
                    storeId={store.id}
                    item={item}
                    categories={categoryList}
                    attributes={attributeList}
                    onCancel={() => setEditing(null)}
                    onSave={(input, categoryIds, values) =>
                      run(async () => {
                        await api.updateStoreProduct(store.id, item.id, input);
                        await api.assignCategories(
                          store.id,
                          item.id,
                          categoryIds,
                        );
                        await api.setProductAttributes(
                          store.id,
                          item.id,
                          values,
                        );
                        setEditing(null);
                      })
                    }
                  />
                ) : (
                  <tr key={item.id}>
                    <td>{item.name}</td>
                    <td>{item.sku}</td>
                    <td>{money.format(item.price)}</td>
                    <td>{item.vatRate}%</td>
                    <td>
                      {item.isVisible ? t("common:yes") : t("common:hidden")}
                    </td>
                    <td>
                      {categoryList
                        .filter((category) =>
                          item.categoryIds.includes(category.id),
                        )
                        .map((category) => category.name)
                        .join(", ")}
                    </td>
                    <td>
                      <button type="button" onClick={() => setEditing(item.id)}>
                        {t("common:edit")}
                      </button>
                    </td>
                  </tr>
                ),
              )}
            </tbody>
          </table>
        </section>
      )}
    </StoreRoute>
  );
}

export function StoreOrdersPage() {
  const { t } = useTranslation("orders");
  const { user } = useSession();
  const { store } = useSelectedStore();
  return (
    <div className="admin-route-section">
      <h1>{t("title")}</h1>
      <OrdersSection storeId={store.id} canManage={canManageStore(user.role)} />
    </div>
  );
}

export function StoreReturnsPage() {
  const { t, i18n } = useTranslation("returns");
  const { user } = useSession();
  const { store } = useSelectedStore();
  return (
    <StoreRoute title={t("title")} allowed={canManageStore(user.role)}>
      <ReturnsSection
        storeId={store.id}
        money={currencyFormatter(store.currency, i18n.resolvedLanguage)}
      />
    </StoreRoute>
  );
}

export function StoreReviewsPage() {
  const { t } = useTranslation("reviews");
  const { user } = useSession();
  const { store } = useSelectedStore();
  return (
    <StoreRoute title={t("title")} allowed={canManageCatalog(user.role)}>
      <ReviewsSection storeId={store.id} />
    </StoreRoute>
  );
}

export function StoreOperationsPage() {
  const { t } = useTranslation("operations");
  const { user } = useSession();
  const { store } = useSelectedStore();
  return (
    <StoreRoute title={t("title")} allowed={canManageStore(user.role)}>
      <FailedMessagesSection storeId={store.id} />
    </StoreRoute>
  );
}

function requestError<T>(state: RequestState<T>, reload: () => void) {
  if (state.status === "error" || state.status === "not-found")
    return (
      <RequestError error={state.error} operation="read" onRetry={reload} />
    );
  if (state.status === "ready" && state.refreshError !== null)
    return (
      <RequestError
        error={state.refreshError}
        operation="read"
        onRetry={reload}
      />
    );
  return null;
}

type EditRowProps = {
  storeId: string;
  item: StoreProduct;
  categories: Category[];
  attributes: AttributeDefinition[];
  onCancel: () => void;
  onSave: (
    input: StoreProductInput,
    categoryIds: string[],
    values: AttributeValues,
  ) => void;
};

function EditRow({
  storeId,
  item,
  categories,
  attributes,
  onCancel,
  onSave,
}: EditRowProps) {
  const { t } = useTranslation(["listings", "categories", "common", "errors"]);
  const [current] = useRequest(`product-attributes:${item.id}`, (signal) =>
    api.productAttributes(storeId, item.id, signal),
  );
  function save(form: FormData) {
    onSave(
      {
        name: String(form.get("name")),
        slug: String(form.get("slug")),
        description: String(form.get("description")) || null,
        price: Number(form.get("price")),
        vatRate: Number(form.get("vatRate")),
        isVisible: form.get("isVisible") === "on",
        sortOrder: item.sortOrder,
      },
      form.getAll("categoryIds").map(String),
      readAttributeValues(form, attributes),
    );
  }
  if (current.status !== "ready")
    return (
      <tr>
        <td colSpan={7}>
          {current.status === "error" || current.status === "not-found"
            ? t("errors:request")
            : t("listings:loadingAttributes")}
        </td>
      </tr>
    );
  return (
    <tr>
      <td colSpan={7}>
        <form action={save} className="stack edit-form">
          <label>
            {t("listings:name")}{" "}
            <input name="name" defaultValue={item.name} required />
          </label>
          <label>
            {t("listings:slug")}{" "}
            <input name="slug" defaultValue={item.slug} required />
          </label>
          <label>
            {t("listings:description")}{" "}
            <textarea
              name="description"
              defaultValue={item.description ?? ""}
              rows={3}
            />
          </label>
          <label>
            {t("listings:price")}{" "}
            <input
              name="price"
              type="number"
              min="0"
              step="0.01"
              defaultValue={item.price}
              required
            />
          </label>
          <label>
            {t("listings:vatRate")}{" "}
            <input
              name="vatRate"
              type="number"
              min="0"
              max="100"
              step="0.01"
              defaultValue={item.vatRate}
              required
            />
          </label>
          <label>
            <input
              name="isVisible"
              type="checkbox"
              defaultChecked={item.isVisible}
            />{" "}
            {t("listings:storefrontVisible")}
          </label>
          <fieldset>
            <legend>{t("categories:title")}</legend>
            {categories.map((category) => (
              <label key={category.id}>
                <input
                  name="categoryIds"
                  type="checkbox"
                  value={category.id}
                  defaultChecked={item.categoryIds.includes(category.id)}
                />{" "}
                {category.name}
              </label>
            ))}
          </fieldset>
          {attributes.length > 0 && (
            <AttributeValueFields
              attributes={attributes}
              values={current.data.values}
            />
          )}
          <p className="inline-form">
            <button type="submit">{t("common:save")}</button>
            <button type="button" onClick={onCancel}>
              {t("common:cancel")}
            </button>
          </p>
        </form>
      </td>
    </tr>
  );
}
