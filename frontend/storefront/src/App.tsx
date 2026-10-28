import { lazy, Suspense, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { BrowserRouter, Route, Routes } from "react-router";
import { CartProvider } from "./components/CartProvider";
import { CustomerProvider } from "./components/CustomerProvider";
import { Layout } from "./components/Layout";
import { Message } from "./components/Message";
import { ProductDetailLoading } from "./components/product/ProductDetailLoading";
import { ShoppingPageLoading } from "./components/ShoppingPageLoading";
import { LoadingState } from "./components/ui/LoadingState";
import { RequestError } from "./components/ui/RequestError";
import { ProductListPage } from "./pages/ProductListPage";
import { applyStore, fetchStore, type Store } from "./store";
import { StoreContext } from "./storeContext";
import { statusOf } from "./api/errors";
import { publicRoutes } from "./publicPages";
import { installPageMetadata } from "./pageMetadata";
import type { EmailChangeEntry } from "./emailChangeEntry";

const ProductDetailPage = lazy(() => import("./pages/ProductDetailPage").then(module => ({ default: module.ProductDetailPage })));
const CartPage = lazy(() => import("./pages/CartPage").then(module => ({ default: module.CartPage })));
const CheckoutPage = lazy(() => import("./pages/CheckoutPage").then(module => ({ default: module.CheckoutPage })));
const ContentPage = lazy(() => import("./pages/ContentPage").then(module => ({ default: module.ContentPage })));
const AccountConsentsPage = lazy(() => import("./pages/AccountConsentsPage").then(module => ({ default: module.AccountConsentsPage })));
const AccountPrivacyPage = lazy(() => import("./pages/AccountPrivacyPage").then(module => ({ default: module.AccountPrivacyPage })));
const AccountEmailPage = lazy(() => import("./pages/AccountEmailPage").then(module => ({ default: module.AccountEmailPage })));
const ResendVerificationPage = lazy(() => import("./pages/ResendVerificationPage").then(module => ({ default: module.ResendVerificationPage })));
const ConfirmEmailPage = lazy(() => import("./pages/ConfirmEmailPage").then(module => ({ default: module.ConfirmEmailPage })));
const AccountOrderPage = lazy(() => import("./pages/AccountOrderPage").then((module) => ({ default: module.AccountOrderPage })));
const AccountPage = lazy(() => import("./pages/AccountPage").then((module) => ({ default: module.AccountPage })));
const ForgotPasswordPage = lazy(() => import("./pages/ForgotPasswordPage").then((module) => ({ default: module.ForgotPasswordPage })));
const OrderPage = lazy(() => import("./pages/OrderPage").then((module) => ({ default: module.OrderPage })));
const RegisterPage = lazy(() => import("./pages/RegisterPage").then((module) => ({ default: module.RegisterPage })));
const ResetPasswordPage = lazy(() => import("./pages/ResetPasswordPage").then((module) => ({ default: module.ResetPasswordPage })));
const SignInPage = lazy(() => import("./pages/SignInPage").then((module) => ({ default: module.SignInPage })));
const VerifyEmailPage = lazy(() => import("./pages/VerifyEmailPage").then((module) => ({ default: module.VerifyEmailPage })));

type StoreState =
  | { status: "loading" }
  | { status: "ready"; store: Store }
  | { status: "not-found" }
  | { status: "unavailable"; error: unknown };

function App({ emailChangeEntry = null }: { emailChangeEntry?: EmailChangeEntry | null }) {
  const { t, i18n } = useTranslation(["common", "errors", "catalog", "cart", "checkout"]);
  const [state, setState] = useState<StoreState>({ status: "loading" });
  const [version, setVersion] = useState(0);

  useEffect(() => {
    if (state.status === "ready") return;
    const title =
      state.status === "loading"
        ? t("common:loadingStore")
        : state.status === "not-found"
          ? t("errors:storeNotFoundTitle")
          : t("errors:storeUnavailableTitle");
    document.title = title;
    return installPageMetadata(null);
  }, [i18n.resolvedLanguage, state.status, t]);

  useEffect(() => {
    const controller = new AbortController();
    fetchStore(controller.signal)
      .then((store) => {
        if (!controller.signal.aborted) {
          applyStore(store);
          setState({ status: "ready", store });
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setState(
            statusOf(error) === 404
              ? { status: "not-found" }
              : { status: "unavailable", error },
          );
        }
      });
    return () => controller.abort();
  }, [version]);

  switch (state.status) {
    case "loading":
      return (
        <main className="app container app-boot">
          <LoadingState label={t("common:loadingStore")} lines={4} />
        </main>
      );
    case "not-found":
      return (
        <main className="app container app-boot">
          <Message
            title={t("errors:storeNotFoundTitle")}
            text={t("errors:storeNotFoundBody")}
          />
        </main>
      );
    case "unavailable":
      return (
        <main className="app container app-boot">
          <Message
            title={t("errors:storeUnavailableTitle")}
            text={t("errors:genericBody")}
          />
          <RequestError
            error={state.error}
            operation="read"
            onRetry={() => {
              setState({ status: "loading" });
              setVersion((current) => current + 1);
            }}
          />
        </main>
      );
    case "ready":
      return (
        <StoreContext value={state.store}>
          <CustomerProvider>
            <CartProvider>
              <BrowserRouter>
                <Routes>
                  <Route element={<Layout />}>
                    <Route index element={<ProductListPage />} />
                    <Route path={publicRoutes.category} element={<ProductListPage />} />
                    <Route path={publicRoutes.product} element={
                      <Suspense fallback={<ProductDetailLoading label={t("catalog:loadingProduct")} />}>
                        <ProductDetailPage />
                      </Suspense>
                    } />
                    <Route path={publicRoutes.contentPage} element={<ContentPage />} />
                    <Route path="cart" element={
                      <Suspense fallback={<ShoppingPageLoading kind="cart" label={t("cart:loading")} />}>
                        <CartPage />
                      </Suspense>
                    } />
                    <Route path="checkout" element={
                      <Suspense fallback={<ShoppingPageLoading kind="checkout" label={t("checkout:loadingCart")} />}>
                        <CheckoutPage />
                      </Suspense>
                    } />
                    <Route path="order/:number" element={<OrderPage />} />
                    <Route path="account" element={<AccountPage />} />
                    <Route path="account/consents" element={<AccountConsentsPage />} />
                    <Route path="account/privacy" element={<AccountPrivacyPage />} />
                    <Route path="account/email" element={<AccountEmailPage />} />
                    <Route path="account/resend-verification" element={<ResendVerificationPage />} />
                    <Route path="account/confirm-email" element={<ConfirmEmailPage entry={emailChangeEntry} />} />
                    <Route
                      path="account/orders/:number"
                      element={<AccountOrderPage />}
                    />
                    <Route path="account/sign-in" element={<SignInPage />} />
                    <Route path="account/register" element={<RegisterPage />} />
                    <Route
                      path="account/verify"
                      element={<VerifyEmailPage />}
                    />
                    <Route
                      path="account/forgot-password"
                      element={<ForgotPasswordPage />}
                    />
                    <Route
                      path="account/reset-password"
                      element={<ResetPasswordPage />}
                    />
                    <Route
                      path="*"
                      element={
                        <Message
                          title={t("errors:pageNotFoundTitle")}
                          text={t("errors:pageNotFoundBody")}
                        />
                      }
                    />
                  </Route>
                </Routes>
              </BrowserRouter>
            </CartProvider>
          </CustomerProvider>
        </StoreContext>
      );
  }
}

export default App;
