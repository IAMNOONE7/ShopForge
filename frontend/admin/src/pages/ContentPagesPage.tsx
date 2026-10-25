import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { useSelectedStore } from "../adminContext";
import { contentPagesApi, isContentPageId, type ContentPage, type ContentPageInput } from "../api/contentPages";
import { statusOf } from "../api/errors";
import { isHttpError } from "../api/http";
import { ContentPageForm } from "../components/ContentPageForm";
import { contentDraft, contentInput, contentParagraphs } from "../components/contentPageDraft";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useSetupProgress } from "../useSetupProgress";
import { ForbiddenPage } from "./RouteStatePage";
import "./ContentPagesPage.css";

type Mode = "list" | "new" | "detail";
type Action = "publish" | "unpublish" | "delete";
type Review = { kind: Action; page: ContentPage; storeName: string };
type Outcome = { kind: "refused" | "uncertain"; error: unknown };
type Notice = "created" | "saved" | "published" | "unpublished" | "deleted";
export function ContentPagesPage() { return <ContentRoute mode="list" />; }
export function NewContentPage() { return <ContentRoute mode="new" />; }
export function ContentPageDetailPage() { return <ContentRoute mode="detail" />; }
function ContentRoute({ mode }: { mode: Mode }) {
  const { user } = useSession(), { store } = useSelectedStore(), { pageId = "" } = useParams(), { t } = useTranslation("contentPages");
  if (!canManageStore(user.role)) return <ForbiddenPage />;
  if (mode === "detail" && !isContentPageId(pageId)) return <div className="content-workspace"><h1>{t("detailTitle")}</h1><Link to={`/stores/${store.id}/pages`}>{t("back")}</Link><EmptyState title={t("missingTitle")}>{t("missingBody")}</EmptyState></div>;
  return <ContentWorkspace key={`${user.tenantId}:${user.id}:${user.role}:${store.id}:${mode}:${pageId.toLowerCase()}`} mode={mode} pageId={pageId.toLowerCase()} />;
}
function ContentWorkspace({ mode, pageId }: { mode: Mode; pageId: string }) {
  const { store, storeRefreshing, storeRefreshError } = useSelectedStore(), { t } = useTranslation("contentPages");
  const navigate = useNavigate(), location = useLocation(), base = `/stores/${store.id}/pages`;
  const read = useSetupProgress(`content:${store.id}`, signal => contentPagesApi.list(store.id, signal), () => false);
  const [editor, setEditor] = useState<{ page: ContentPage; storeName: string } | null>(null), [review, setReview] = useState<Review | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null), [uncertain, setUncertain] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(() => ["created", "deleted"].includes(location.state?.contentPageNotice) ? location.state.contentPageNotice as Notice : null);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const mounted = useRef(false), controller = useRef<AbortController | null>(null), opener = useRef<HTMLButtonElement | null>(null);
  const editButton = useRef<HTMLButtonElement>(null);
  const heading = useRef<HTMLHeadingElement>(null), reviewHeading = useRef<HTMLHeadingElement>(null), result = useRef<HTMLDivElement>(null);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (review) reviewHeading.current?.focus(); }, [review]);
  useEffect(() => { if (outcome) result.current?.focus(); }, [outcome]);
  const state = read.state, error = state.status === "ready" ? state.refreshError : state.status === "loading" ? null : state.error;
  const visible = state.status === "ready" && ![401, 403, 404].includes(statusOf(error) ?? 0);
  const pages = visible && state.status === "ready" ? state.data : [], item = pages.find(page => page.id.toLowerCase() === pageId);
  const storeCurrent = !storeRefreshing && storeRefreshError === null;
  const fresh = visible && state.status === "ready" && !state.refreshing && error === null && !read.suspended;
  const disabled = !fresh || !storeCurrent || pending || uncertain;
  const changed = editor !== null && (editor.storeName !== store.name || JSON.stringify(editor.page) !== JSON.stringify(item));
  const reviewChanged = review !== null && (review.storeName !== store.name || JSON.stringify(review.page) !== JSON.stringify(item));
  const serverErrors = outcome && isHttpError(outcome.error) ? outcome.error.problem?.errors : undefined;
  const duplicate = outcome && isHttpError(outcome.error) && outcome.error.status === 409 && outcome.error.problem?.title === "The slug is already used in this store";
  const fieldErrors = mode !== "list" && (mode === "new" || editor) ? (["title", "slug", "body"] as const)
    .filter(field => serverErrors && Object.hasOwn(serverErrors, field) || field === "slug" && duplicate)
    .map(field => ({ field: `content-${field}`, message: t(field === "slug" && duplicate ? "slugTaken" : `${field}Invalid`) })) : [];

  function close() {
    if (pending) return;
    setEditor(null); setReview(null); setOutcome(null);
    window.requestAnimationFrame(() => {
      if (!mounted.current) return;
      const target = opener.current?.isConnected ? opener.current : editor ? editButton.current : null;
      if (target && !target.disabled) target.focus(); else heading.current?.focus();
    });
  }
  async function change(input?: ContentPageInput) {
    if (mode === "list" || disabled || state.status !== "ready" || !read.isCurrent(state.data)) return;
    if (mode === "new" && !input || mode === "detail" && (!item || editor && changed || review && reviewChanged)) return;
    if (mode === "detail" && (input ? !editor : !review)) return;
    const target = review, id = item?.id;
    const body = input ?? (target ? { ...contentInput(contentDraft(target.page), target.page), isPublished: target.kind === "publish" } : undefined);
    await run(async () => {
      read.suspend(); setOutcome(null); setNotice(null);
      const request = new AbortController(); controller.current = request;
      let navigating = false;
      try {
        if (target?.kind === "delete") {
          await contentPagesApi.remove(store.id, target.page.id, request.signal);
          if (mounted.current) { navigating = true; await navigate(base, { state: { contentPageNotice: "deleted" } }); }
        } else {
          const saved = mode === "new" ? await contentPagesApi.create(store.id, body!, request.signal)
            : await contentPagesApi.update(store.id, id!, body!, request.signal);
          if (!mounted.current) return;
          if (mode === "new") { navigating = true; await navigate(`${base}/${saved.id}`, { state: { contentPageNotice: "created" } }); }
          else {
            read.accept(pages.map(page => page.id === saved.id ? saved : page)); setEditor(null); setReview(null);
            setNotice(target ? target.kind === "publish" ? "published" : "unpublished" : "saved"); heading.current?.focus();
          }
        }
      } catch (error) {
        navigating = false;
        if (!mounted.current) return;
        const refused = isHttpError(error) && error.kind === "http" && [400, 401, 403, 404, 409, 429].includes(error.status ?? 0);
        setUncertain(!refused); setReview(null); setOutcome({ kind: refused ? "refused" : "uncertain", error });
      } finally {
        controller.current = null;
        if (mounted.current && !navigating) read.resume();
      }
    });
  }
  return <div className="content-workspace">
    {mode !== "list" && <Link to={base}>{t("back")}</Link>}
    <div className="content-heading"><h1 ref={heading} tabIndex={-1}>{t(mode === "list" ? "listTitle" : mode === "new" ? "newTitle" : "detailTitle")}</h1>
      <Button type="button" variant="secondary" disabled={pending || read.refreshing} onClick={read.refresh}>{t("refresh")}</Button></div>
    <p className="hint">{t("scope", { store: store.name })}</p>
    <p className="hint">{t("publicationHint")}</p>
    {!storeCurrent && <p role="status">{t("storeStale")}</p>}
    {read.refreshing && <LoadingState label={t("loading")} lines={3} />}
    {error !== null && <RequestError error={error} operation="read" onRetry={read.refresh} />}
    {!fresh && visible && !pending && <p role="status">{t("stale")}</p>}
    {notice && <p className="content-success" role="status">{t(`notices.${notice}`)}</p>}
    {outcome && visible && <div ref={result} className="content-outcome" tabIndex={-1}><p role="status">{t(outcome.kind)}</p>
      <RequestError error={outcome.error} operation="write" fieldErrors={fieldErrors} />
    </div>}
    {uncertain && <section className="content-panel content-recovery"><p>{t("reviewRead")}</p><Link to={base}>{t("back")}</Link>
      <Button type="button" variant="secondary" disabled={!fresh || !storeCurrent || pending} onClick={() => {
        if (state.status === "ready" && read.isCurrent(state.data) && storeCurrent && !pending) { setUncertain(false); setOutcome(null); heading.current?.focus(); }
      }}>{t("finishReview")}</Button></section>}
    {visible && mode === "list" && <>
      <Link to={`${base}/new`} className="button">{t("newTitle")}</Link>
      {pages.length === 0 && fresh && <EmptyState title={t("emptyTitle")}>{t("emptyHint")}</EmptyState>}
      <ul className="content-page-list">{pages.map(page => <li key={page.id} className="content-panel"><div>
        <h2 lang={store.culture}><Link to={`${base}/${page.id}`}>{page.title}</Link></h2><p><code>/pages/{page.slug}</code></p>
      </div><span className={`content-state content-state-${page.isPublished ? "published" : "draft"}`}>{t(page.isPublished ? "published" : "draft")}</span></li>)}</ul>
    </>}
    {visible && mode === "detail" && !item && !editor && <EmptyState title={t("missingTitle")}>{t("missingBody")}</EmptyState>}
    {visible && (mode === "new" || editor) && <>
      {changed && <p role="status">{t("changed")}</p>}
      <ContentPageForm page={editor?.page} pages={pages} culture={store.culture} disabled={disabled || changed} pending={pending} onSave={change}
        onEdit={() => { if (outcome?.kind === "refused") setOutcome(null); }}
        onCancel={mode === "detail" ? close : undefined} />
    </>}
    {visible && mode === "detail" && item && !editor && <>
      <section className="content-panel content-details"><div className="content-heading"><h2 lang={store.culture}>{item.title}</h2><span className="content-state">{t(item.isPublished ? "published" : "draft")}</span></div>
        <p className="content-path"><span>{t("path")}</span> <code>/pages/{item.slug}</code></p>
        <div className="cluster"><Button ref={editButton} type="button" variant="secondary" disabled={disabled || review !== null} onClick={event => {
          if (disabled || state.status !== "ready" || !read.isCurrent(state.data)) return;
          opener.current = event.currentTarget; setOutcome(null); setNotice(null); setEditor({ page: structuredClone(item), storeName: store.name });
        }}>{t("edit")}</Button>
          {([item.isPublished ? "unpublish" : "publish", "delete"] as const).map(kind => <Button key={kind} type="button" variant={kind === "delete" ? "quiet" : "primary"}
            disabled={disabled || review !== null} onClick={event => {
              if (disabled || state.status !== "ready" || !read.isCurrent(state.data)) return;
              opener.current = event.currentTarget; setNotice(null); setOutcome(null); setReview({ kind, page: structuredClone(item), storeName: store.name });
            }}>{t(`actions.${kind}`)}</Button>)}
        </div>
        <article className="content-text-preview" lang={store.culture} aria-label={t("savedText")}>
          {contentParagraphs(item.body).map((paragraph, index) => <p key={index}>{paragraph}</p>)}{!item.body.trim() && <p>{t("emptyBody")}</p>}
        </article>
        <details className="content-seo"><summary>{t("metadata")}</summary><p className="hint">{t("metadataHint")}</p><dl>
          <div><dt>{t("metadataTitle")}</dt><dd>{item.seoTitle ?? t("notSet")}</dd></div>
          <div><dt>{t("metadataDescription")}</dt><dd>{item.seoDescription ?? t("notSet")}</dd></div>
          <div><dt>{t("metadataNoIndex")}</dt><dd>{t(item.seoNoIndex ? "requested" : "notRequested")}</dd></div>
        </dl></details>
      </section>
    </>}
    {visible && review && <form className="content-panel content-confirmation" noValidate aria-labelledby="content-confirm-title" onSubmit={event => { event.preventDefault(); void change(); }}
      onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); close(); } }}>
      <h2 id="content-confirm-title" ref={reviewHeading} tabIndex={-1}>{t(`confirmTitles.${review.kind}`)}</h2>
      <p>{t("scope", { store: review.storeName })}</p><p lang={store.culture}><strong>{review.page.title}</strong></p><code>/pages/{review.page.slug}</code>
      <p>{t(`confirmHints.${review.kind}`)}</p>{reviewChanged && <p role="status">{t("changed")}</p>}
      <div className="cluster"><Button type="submit" disabled={disabled || reviewChanged} busy={pending} busyLabel={t("saving")} variant={review.kind === "delete" ? "danger" : "primary"}>{t(`confirmActions.${review.kind}`)}</Button>
        <Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancel")}</Button></div>
    </form>}
  </div>;
}
