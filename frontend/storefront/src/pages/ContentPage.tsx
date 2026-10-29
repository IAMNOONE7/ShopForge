import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { getContentPage } from "../api";
import { Message } from "../components/Message";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { StoreInformationLinks } from "../components/StoreInformationLinks";
import { storeInformationPages } from "../publicPages";
import { useStore } from "../storeContext";
import { useRequest } from "../useRequest";
import { usePageMetadata } from "../usePageMetadata";
import "../styles/information.css";

export function ContentPage() {
  const { t } = useTranslation(["content", "errors", "navigation"]);
  const store = useStore();
  const { slug = "" } = useParams();
  const information = storeInformationPages.find(page => page.slug === slug);
  const heading = useRef<HTMLHeadingElement>(null);
  const retry = useRef<string | null>(null);
  const page = useRequest(`content:${slug}`, (signal) =>
    getContentPage(slug, signal),
  );
  usePageMetadata(page.status === "ready" && !page.refreshing && page.refreshError === null ? page.data : null, "content");
  useEffect(() => { retry.current = null; }, [slug]);
  useEffect(() => {
    if (retry.current === slug && page.status !== "loading") {
      retry.current = null;
      heading.current?.focus();
    }
  }, [page.status, slug]);

  if (information) {
    const title = information.label === "contact" ? t("navigation:contact") : t("navigation:about");
    const missing = information.label === "contact" ? t("content:contactUnavailable") : t("content:aboutUnavailable");
    return <article className="content-page store-information-page" aria-busy={page.status === "loading" || undefined}>
      <Link className="information-back" to="/"><span aria-hidden="true">←</span> {t("navigation:allProducts")}</Link>
      <header className="information-page-heading">
        <span className="customer-eyebrow" lang={store.culture}>{store.name}</span>
        <h1 ref={heading} tabIndex={-1} lang={page.status === "ready" ? store.culture : undefined}>
          {page.status === "ready" ? page.data.title : title}
        </h1>
      </header>
      <StoreInformationLinks className="information-page-links" />
      {page.status === "loading" ? <LoadingState label={t("content:loading")} />
        : page.status === "error" ? <RequestError error={page.error} operation="read" onRetry={() => { retry.current = slug; page.reload(); }} />
          : page.status === "not-found" || !page.data.body.trim() ? <p className="information-unavailable">{missing}</p>
            : <div className="content-body" lang={store.culture}>
              {paragraphs(page.data.body).map((paragraph, index) => <p key={index}>{paragraph}</p>)}
            </div>}
    </article>;
  }

  switch (page.status) {
    case "loading":
      return <LoadingState label={t("content:loading")} />;
    case "not-found":
      return (
        <Message title={t("content:notFoundTitle")} text={t("content:notFoundBody")} />
      );
    case "error":
      return (
        <section className="content-page">
          <h1>{t("content:unavailableTitle")}</h1>
          <RequestError error={page.error} operation="read" onRetry={page.reload} />
        </section>
      );
    case "ready":
      return (
        <article className="content-page">
          <h1 lang={store.culture}>{page.data.title}</h1>
          {paragraphs(page.data.body).map((paragraph, index) => (
            <p key={index}>{paragraph}</p>
          ))}
        </article>
      );
  }
}

// A blank line starts a new paragraph, which is the whole of the format. React puts the text on the page as
// text, so a merchant who types a tag sees a tag.
function paragraphs(body: string) {
  return body
    .split(/\n\s*\n/)
    .map((paragraph) => paragraph.trim())
    .filter((paragraph) => paragraph.length > 0);
}
