import { useTranslation } from "react-i18next";
import { useParams } from "react-router";
import { getContentPage } from "../api";
import { Message } from "../components/Message";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useRequest } from "../useRequest";

export function ContentPage() {
  const { t } = useTranslation(["content", "errors"]);
  const { slug = "" } = useParams();
  const page = useRequest(`content:${slug}`, (signal) =>
    getContentPage(slug, signal),
  );

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
          <h1>{page.data.title}</h1>
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
