import { Component, Suspense, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";
import { LoadingState } from "./ui/LoadingState";

export function RouteContent({ children }: { children: ReactNode }) {
  const { t } = useTranslation(["common", "errors"]);
  const location = useLocation();
  return (
    <PageBoundary key={location.pathname} fallback={
      <section className="route-load-error" role="alert">
        <h1>{t("errors:pageLoadTitle")}</h1>
        <p>{t("errors:pageLoadBody")}</p>
        <button type="button" onClick={() => window.location.reload()}>{t("errors:reloadPage")}</button>
      </section>
    }>
      <Suspense fallback={<LoadingState label={t("common:loading")} lines={4} />}>
        {children}
      </Suspense>
    </PageBoundary>
  );
}

class PageBoundary extends Component<{ children: ReactNode; fallback: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}
