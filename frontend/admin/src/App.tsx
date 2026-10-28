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
import { ProductStockPage } from "./pages/ProductStockPage";
import { ForbiddenPage, NotFoundPage } from "./pages/RouteStatePage";
import { StoreIndexPage } from "./pages/StoreIndexPage";
import { StoreOverviewPage } from "./pages/StoreOverviewPage";
import {
  StoreDiscountsPage,
  StoreMethodsPage,
  StoreReviewsPage,
  StoreSettingsPage,
} from "./pages/StorePage";
import { adminHomePath, safeAdminReturnPath } from "./routing";
import { canManageCatalog, canManageStore, SessionContext } from "./session";
import { isOperatorPath } from "./platform/routing";
import type { InvitationEntry } from "./invitationEntry";
import type { StaffRecoveryEntry } from "./staffRecoveryEntry";

const ForgotStaffPasswordPage = lazy(() => import("./pages/StaffPasswordPages").then(pages => ({ default: pages.ForgotStaffPasswordPage })));
const ResetStaffPasswordPage = lazy(() => import("./pages/StaffPasswordPages").then(pages => ({ default: pages.ResetStaffPasswordPage })));
const ColleaguesPage = lazy(() => import("./pages/ColleaguesPage").then(pages => ({ default: pages.ColleaguesPage })));
const StaffAccountPage = lazy(() => import("./pages/StaffAccountPage").then(pages => ({ default: pages.StaffAccountPage })));
const StaffTwoFactorPage = lazy(() => import("./pages/StaffTwoFactorPage").then(pages => ({ default: pages.StaffTwoFactorPage })));
const PlanUsagePage = lazy(() => import("./pages/PlanUsagePage").then(pages => ({ default: pages.PlanUsagePage })));
const InvitationAcceptancePage = lazy(() => import("./pages/InvitationAcceptancePage").then(pages => ({ default: pages.InvitationAcceptancePage })));

const PlatformApp = lazy(() => import("./platform/PlatformApp"));
const CategoriesPage = lazy(() => import("./pages/CategoryPages").then((pages) => ({ default: pages.CategoriesPage })));
const NewCategoryPage = lazy(() => import("./pages/CategoryPages").then((pages) => ({ default: pages.NewCategoryPage })));
const CategoryDetailPage = lazy(() => import("./pages/CategoryPages").then((pages) => ({ default: pages.CategoryDetailPage })));
const ListingsPage = lazy(() => import("./pages/ListingPages").then((pages) => ({ default: pages.ListingsPage })));
const StoreDomainsPage = lazy(() => import("./pages/StoreDomainsPage").then((pages) => ({ default: pages.StoreDomainsPage })));
const StoreEmailPage = lazy(() => import("./pages/StoreEmailPage").then(pages => ({ default: pages.StoreEmailPage })));
const StoreOrdersPage = lazy(() => import("./pages/OrderPages").then((pages) => ({ default: pages.StoreOrdersPage })));
const StoreOrderPage = lazy(() => import("./pages/OrderPages").then((pages) => ({ default: pages.StoreOrderPage })));
const UnappliedPaymentsPage = lazy(() => import("./pages/UnappliedPaymentsPage").then((pages) => ({ default: pages.UnappliedPaymentsPage })));
const StoreReturnsPage = lazy(() => import("./pages/ReturnPages").then((pages) => ({ default: pages.StoreReturnsPage })));
const StoreReturnPage = lazy(() => import("./pages/ReturnPages").then((pages) => ({ default: pages.StoreReturnPage })));
const NewListingPage = lazy(() => import("./pages/ListingPages").then((pages) => ({ default: pages.NewListingPage })));
const ListingDetailPage = lazy(() => import("./pages/ListingPages").then((pages) => ({ default: pages.ListingDetailPage })));
const StoreOperationsPage = lazy(() => import("./pages/StoreOperationsPage").then((pages) => ({ default: pages.StoreOperationsPage })));
const StoreSetupPage = lazy(() => import("./pages/StoreSetupPage").then(pages => ({ default: pages.StoreSetupPage })));
const StoreSetupDetailPage = lazy(() => import("./pages/StoreSetupPage").then(pages => ({ default: pages.StoreSetupDetailPage })));
const StoreConnectionsPage = lazy(() => import("./pages/StoreConnectionsPage").then(pages => ({ default: pages.StoreConnectionsPage })));
const StoreTaxClassesPage = lazy(() => import("./pages/StoreTaxClassesPage").then((pages) => ({ default: pages.StoreTaxClassesPage })));
const ContentPagesPage = lazy(() => import("./pages/ContentPagesPage").then(pages => ({ default: pages.ContentPagesPage })));
const NewContentPage = lazy(() => import("./pages/ContentPagesPage").then(pages => ({ default: pages.NewContentPage })));
const ContentPageDetailPage = lazy(() => import("./pages/ContentPagesPage").then(pages => ({ default: pages.ContentPageDetailPage })));
const MetadataPage = lazy(() => import("./pages/MetadataPage").then(pages => ({ default: pages.MetadataPage })));
const AttributesIndexPage = lazy(() => import("./pages/AttributePages").then(pages => ({ default: pages.AttributesIndexPage })));
const AttributeDetailPage = lazy(() => import("./pages/AttributePages").then(pages => ({ default: pages.AttributeDetailPage })));
const NewAttributePage = lazy(() => import("./pages/AttributePages").then(pages => ({ default: pages.NewAttributePage })));
const StoreFeedsPage = lazy(() => import("./pages/StoreFeedsPage").then(pages => ({ default: pages.StoreFeedsPage })));
const StoreExportsPage = lazy(() => import("./pages/StoreExportsPage").then(pages => ({ default: pages.StoreExportsPage })));
const StoreImportPage = lazy(() => import("./pages/StoreImportPage").then(pages => ({ default: pages.StoreImportPage })));
const StoreShippingSettingsPage = lazy(() => import("./pages/StoreShippingSettingsPage").then(pages => ({ default: pages.StoreShippingSettingsPage })));

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

function App({ invitationEntry = null, recoveryEntry = null }: { invitationEntry?: InvitationEntry | null; recoveryEntry?: StaffRecoveryEntry | null }) {
  if (recoveryEntry) return <BrowserRouter basename={import.meta.env.BASE_URL}><Routes>
    <Route path="forgot-password" element={<DeferredPage><ForgotStaffPasswordPage /></DeferredPage>} />
    <Route path="reset-password" element={<DeferredPage><ResetStaffPasswordPage proof={recoveryEntry.kind === "reset" ? recoveryEntry.proof : null} /></DeferredPage>} />
    <Route path="*" element={<NotFoundPage />} />
  </Routes></BrowserRouter>;
  if (invitationEntry) return <BrowserRouter basename={import.meta.env.BASE_URL}><Routes>
    <Route path="invitations/accept" element={<DeferredPage><InvitationAcceptancePage entry={invitationEntry} /></DeferredPage>} />
  </Routes></BrowserRouter>;
  if (isOperatorPath(window.location.pathname)) return <BrowserRouter basename={import.meta.env.BASE_URL}>
    <Routes><Route path="platform/*" element={<DeferredPage><PlatformApp /></DeferredPage>} /></Routes>
  </BrowserRouter>;
  return <MerchantApp />;
}

function MerchantApp() {
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
                <Route path="users" element={canManageStore(session.user.role) ? <DeferredPage><ColleaguesPage /></DeferredPage> : <ForbiddenPage />} />
                <Route path="account" element={<DeferredPage><StaffAccountPage /></DeferredPage>} />
                <Route path="account/two-factor" element={<DeferredPage><StaffTwoFactorPage /></DeferredPage>} />
                <Route path="plan" element={<DeferredPage><PlanUsagePage /></DeferredPage>} />
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
                  <Route path="domains" element={canManageStore(session.user.role) ? <DeferredPage><StoreDomainsPage /></DeferredPage> : <ForbiddenPage />} />
                  <Route path="pages" element={canManageStore(session.user.role) ? <DeferredPage><ContentPagesPage /></DeferredPage> : <ForbiddenPage />} />
                  <Route path="pages/new" element={canManageStore(session.user.role) ? <DeferredPage><NewContentPage /></DeferredPage> : <ForbiddenPage />} />
                  <Route path="pages/:pageId" element={canManageStore(session.user.role) ? <DeferredPage><ContentPageDetailPage /></DeferredPage> : <ForbiddenPage />} />
                  <Route path="metadata" element={<DeferredPage><MetadataPage kind="store" /></DeferredPage>} />
                  <Route path="products/:listingId/metadata" element={<DeferredPage><MetadataPage kind="listing" /></DeferredPage>} />
                  <Route path="categories/:categoryId/metadata" element={<DeferredPage><MetadataPage kind="category" /></DeferredPage>} />
                  <Route path="pages/:pageId/metadata" element={<DeferredPage><MetadataPage kind="page" /></DeferredPage>} />
                  <Route path="products" element={<DeferredPage><ListingsPage /></DeferredPage>} />
                  <Route path="products/new" element={<DeferredPage><NewListingPage /></DeferredPage>} />
                  <Route path="products/:listingId" element={<DeferredPage><ListingDetailPage /></DeferredPage>} />
                  <Route path="categories" element={<DeferredPage><CategoriesPage /></DeferredPage>} />
                  <Route path="categories/new" element={<DeferredPage><NewCategoryPage /></DeferredPage>} />
                  <Route path="categories/:categoryId" element={<DeferredPage><CategoryDetailPage /></DeferredPage>} />
                  <Route path="attributes" element={<DeferredPage><AttributesIndexPage /></DeferredPage>} />
                  <Route path="attributes/new" element={canManageCatalog(session.user.role) ? <DeferredPage><NewAttributePage /></DeferredPage> : <ForbiddenPage />} />
                  <Route path="attributes/:attributeId" element={<DeferredPage><AttributeDetailPage /></DeferredPage>} />
                  <Route path="feeds" element={<DeferredPage><StoreFeedsPage /></DeferredPage>} />
                  <Route path="feeds/:feed" element={<DeferredPage><StoreFeedsPage /></DeferredPage>} />
                  <Route path="exports" element={<DeferredPage><StoreExportsPage /></DeferredPage>} />
                  <Route path="import" element={<DeferredPage><StoreImportPage /></DeferredPage>} />
                  <Route path="methods" element={<StoreMethodsPage />} />
                  <Route path="shipping-settings" element={<DeferredPage><StoreShippingSettingsPage /></DeferredPage>} />
                  <Route path="discounts" element={<StoreDiscountsPage />} />
                  <Route path="orders" element={<DeferredPage><StoreOrdersPage /></DeferredPage>} />
                  <Route path="orders/:number" element={<DeferredPage><StoreOrderPage /></DeferredPage>} />
                  <Route path="unapplied-payments" element={<DeferredPage><UnappliedPaymentsPage /></DeferredPage>} />
                  <Route path="returns" element={<DeferredPage><StoreReturnsPage /></DeferredPage>} />
                  <Route path="returns/:returnId" element={<DeferredPage><StoreReturnPage /></DeferredPage>} />
                  <Route path="reviews" element={<StoreReviewsPage />} />
                  <Route path="operations" element={<DeferredPage><StoreOperationsPage /></DeferredPage>} />
                  <Route path="setup" element={<DeferredPage><StoreSetupPage /></DeferredPage>} />
                  <Route path="email" element={<DeferredPage><StoreEmailPage /></DeferredPage>} />
                  <Route path="setup/:operationId" element={<DeferredPage><StoreSetupDetailPage /></DeferredPage>} />
                  <Route path="provider-connections" element={<DeferredPage><StoreConnectionsPage /></DeferredPage>} />
                  <Route path="tax-classes" element={<DeferredPage><StoreTaxClassesPage /></DeferredPage>} />
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
