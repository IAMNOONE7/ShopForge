import { lazy, useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { api, type CurrentUser } from "./api";
import { statusOf } from "./api/errors";
import { onUnauthorized } from "./api/http";
import { Layout } from "./components/Layout";
import { DeferredPage } from "./components/DeferredPage";
import { LoadingState } from "./components/ui/LoadingState";
import { RequestError } from "./components/ui/RequestError";
import { StoreLayout } from "./layouts/StoreLayout";
import { LoginPage } from "./pages/LoginPage";
import { NewStorePage } from "./pages/NewStorePage";
import { ProductsPage } from "./pages/ProductsPage";
import { NewPhysicalProductPage } from "./pages/NewPhysicalProductPage";
import { PhysicalProductPage } from "./pages/PhysicalProductPage";
import { StockPage } from "./pages/StockPage";
import { AttributesIndexPage, AttributeDetailPage, NewAttributePage } from "./pages/AttributePages";
import { ProductStockPage } from "./pages/ProductStockPage";
import { ForbiddenPage, NotFoundPage } from "./pages/RouteStatePage";
import { StoreIndexPage } from "./pages/StoreIndexPage";
import { StoreOverviewPage } from "./pages/StoreOverviewPage";
import {
  StoreDiscountsPage,
  StoreImportPage,
  StoreMethodsPage,
  StoreOperationsPage,
  StoreOrdersPage,
  StoreProductsPage,
  StoreReturnsPage,
  StoreReviewsPage,
  StoreSettingsPage,
} from "./pages/StorePage";
import { adminHomePath, safeAdminReturnPath } from "./routing";
import { canManageCatalog, canManageStore, SessionContext } from "./session";

const CategoriesPage = lazy(() => import("./pages/CategoryPages").then((pages) => ({ default: pages.CategoriesPage })));
const NewCategoryPage = lazy(() => import("./pages/CategoryPages").then((pages) => ({ default: pages.NewCategoryPage })));
const CategoryDetailPage = lazy(() => import("./pages/CategoryPages").then((pages) => ({ default: pages.CategoryDetailPage })));

type SessionState =
  | { status: "checking" }
  | { status: "signed-out"; expired: boolean; returnPath: string }
  | { status: "signed-in"; user: CurrentUser }
  | { status: "error"; error: unknown };

function currentReturnPath() {
  return safeAdminReturnPath(
    `${window.location.pathname}${window.location.search}${window.location.hash}`,
  );
}

function App() {
  const { t, i18n } = useTranslation(["auth", "errors"]);
  const [session, setSession] = useState<SessionState>({ status: "checking" });
  const [version, setVersion] = useState(0);
  const [logoutError, setLogoutError] = useState<unknown | null>(null);
  const [logoutPending, setLogoutPending] = useState(false);
  const logoutLock = useRef(false);

  useEffect(() => {
    if (session.status === "signed-in") return;
    document.title =
      session.status === "error"
        ? t("errors:unavailableTitle")
        : t("auth:title");
  }, [i18n.resolvedLanguage, session.status, t]);

  useEffect(
    () =>
      onUnauthorized(() => {
        setLogoutError(null);
        setSession({
          status: "signed-out",
          expired: true,
          returnPath: currentReturnPath(),
        });
      }),
    [],
  );

  useEffect(() => {
    const controller = new AbortController();
    api
      .me(controller.signal)
      .then((user) => {
        if (!controller.signal.aborted)
          setSession({ status: "signed-in", user });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setSession(
          statusOf(error) === 401
            ? {
                status: "signed-out",
                expired: false,
                returnPath: currentReturnPath(),
              }
            : { status: "error", error },
        );
      });
    return () => controller.abort();
  }, [version]);

  const logout = useCallback(async () => {
    if (logoutLock.current) return;
    logoutLock.current = true;
    setLogoutPending(true);
    setLogoutError(null);
    try {
      await api.logout();
      setSession({
        status: "signed-out",
        expired: false,
        returnPath: adminHomePath(),
      });
    } catch (error) {
      setLogoutError(error);
    } finally {
      setLogoutPending(false);
      logoutLock.current = false;
    }
  }, []);

  switch (session.status) {
    case "checking":
      return (
        <main className="app container app-boot">
          <LoadingState label={t("auth:checking")} lines={4} />
        </main>
      );
    case "error":
      return (
        <main className="app container app-boot">
          <RequestError
            error={session.error}
            operation="session"
            onRetry={() => {
              setSession({ status: "checking" });
              setVersion((current) => current + 1);
            }}
          />
        </main>
      );
    case "signed-out":
      return (
        <LoginPage
          expired={session.expired}
          onLogin={(user) => {
            window.history.replaceState(null, "", session.returnPath);
            setSession({ status: "signed-in", user });
          }}
        />
      );
    case "signed-in":
      return (
        <SessionContext
          value={{ user: session.user, logout, logoutPending, logoutError }}
        >
          <BrowserRouter basename={import.meta.env.BASE_URL}>
            <Routes>
              <Route element={<Layout />}>
                <Route index element={<Navigate to="/products" replace />} />
                <Route path="products" element={<ProductsPage />} />
                <Route path="products/new" element={
                  canManageCatalog(session.user.role) ? <NewPhysicalProductPage /> : <ForbiddenPage />
                } />
                <Route path="products/:productId" element={<PhysicalProductPage />} />
                <Route path="stock" element={<StockPage />} />
                <Route path="stock/:variantId" element={<ProductStockPage />} />
                <Route path="stores" element={<StoreIndexPage />} />
                <Route
                  path="stores/new"
                  element={
                    canManageStore(session.user.role) ? (
                      <NewStorePage />
                    ) : (
                      <ForbiddenPage />
                    )
                  }
                />
                <Route path="stores/:storeId" element={<StoreLayout />}>
                  <Route index element={<StoreOverviewPage />} />
                  <Route path="settings" element={<StoreSettingsPage />} />
                  <Route path="products" element={<StoreProductsPage />} />
                  <Route path="categories" element={<DeferredPage><CategoriesPage /></DeferredPage>} />
                  <Route path="categories/new" element={<DeferredPage><NewCategoryPage /></DeferredPage>} />
                  <Route path="categories/:categoryId" element={<DeferredPage><CategoryDetailPage /></DeferredPage>} />
                  <Route path="attributes" element={<AttributesIndexPage />} />
                  <Route path="attributes/new" element={canManageCatalog(session.user.role) ? <NewAttributePage /> : <ForbiddenPage />} />
                  <Route path="attributes/:attributeId" element={<AttributeDetailPage />} />
                  <Route path="import" element={<StoreImportPage />} />
                  <Route path="methods" element={<StoreMethodsPage />} />
                  <Route path="discounts" element={<StoreDiscountsPage />} />
                  <Route path="orders" element={<StoreOrdersPage />} />
                  <Route path="returns" element={<StoreReturnsPage />} />
                  <Route path="reviews" element={<StoreReviewsPage />} />
                  <Route path="operations" element={<StoreOperationsPage />} />
                  <Route path="*" element={<NotFoundPage />} />
                </Route>
                <Route path="*" element={<NotFoundPage />} />
              </Route>
            </Routes>
          </BrowserRouter>
        </SessionContext>
      );
  }
}

export default App;
