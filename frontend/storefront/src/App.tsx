import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { BrowserRouter, Route, Routes } from "react-router";
import { CartProvider } from "./components/CartProvider";
import { CustomerProvider } from "./components/CustomerProvider";
import { Layout } from "./components/Layout";
import { Message } from "./components/Message";
import { LoadingState } from "./components/ui/LoadingState";
import { RequestError } from "./components/ui/RequestError";
import { AccountOrderPage } from "./pages/AccountOrderPage";
import { AccountPage } from "./pages/AccountPage";
import { CartPage } from "./pages/CartPage";
import { CheckoutPage } from "./pages/CheckoutPage";
import { ForgotPasswordPage } from "./pages/ForgotPasswordPage";
import { OrderPage } from "./pages/OrderPage";
import { ProductDetailPage } from "./pages/ProductDetailPage";
import { ProductListPage } from "./pages/ProductListPage";
import { RegisterPage } from "./pages/RegisterPage";
import { ResetPasswordPage } from "./pages/ResetPasswordPage";
import { SignInPage } from "./pages/SignInPage";
import { VerifyEmailPage } from "./pages/VerifyEmailPage";
import { applyStore, fetchStore, type Store } from "./store";
import { StoreContext } from "./storeContext";
import { statusOf } from "./api/errors";

type StoreState =
  | { status: "loading" }
  | { status: "ready"; store: Store }
  | { status: "not-found" }
  | { status: "unavailable"; error: unknown };

function App() {
  const { t, i18n } = useTranslation(["common", "errors"]);
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
                    <Route path="c/:slug" element={<ProductListPage />} />
                    <Route path="p/:slug" element={<ProductDetailPage />} />
                    <Route path="cart" element={<CartPage />} />
                    <Route path="checkout" element={<CheckoutPage />} />
                    <Route path="order/:number" element={<OrderPage />} />
                    <Route path="account" element={<AccountPage />} />
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
