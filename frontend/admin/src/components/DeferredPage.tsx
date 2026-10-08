import { Component, Suspense, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

class PageBoundary extends Component<{ children: ReactNode; failed: (error: unknown) => ReactNode }, { error: unknown | null }> {
  state: { error: unknown | null } = { error: null };
  static getDerivedStateFromError(error: unknown) { return { error }; }
  render() { return this.state.error !== null ? this.props.failed(this.state.error) : this.props.children; }
}

export function DeferredPage({ children }: { children: ReactNode }) {
  const { t } = useTranslation("common");
  const location = useLocation();
  return <PageBoundary key={location.pathname} failed={(error) => <RequestError error={error} operation="read" onRetry={() => window.location.reload()} />}>
    <Suspense fallback={<LoadingState label={t("loading")} lines={4} />}>{children}</Suspense>
  </PageBoundary>;
}
