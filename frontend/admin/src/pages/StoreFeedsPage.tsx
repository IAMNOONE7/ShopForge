import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { feedsApi, type FeedCategory, type StoreFeed } from "../api/feeds";
import { isHttpError } from "../api/http";
import { statusOf } from "../api/errors";
import { FeedAddress, FeedChecks, FeedFacts } from "../components/FeedReadouts";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, useSession } from "../session";
import { useSetupProgress } from "../useSetupProgress";
import "./StoreFeedsPage.css";

type Editor = { before: StoreFeed } & (
  | { kind: "settings"; enabled: boolean }
  | { kind: "rotate" }
  | { kind: "mapping"; category: FeedCategory; value: string }
);
type Command = { kind: "settings"; enabled: boolean } | { kind: "rotate" } | { kind: "run" } | { kind: "mapping"; categoryId: string; value: string | null };
const knownNames = new Map([["google", "Google Merchant Center"], ["heureka", "Heureka"], ["zbozi", "Zboží.cz"]]);
const feedName = (key: string) => knownNames.get(key) ?? key;
const denied = (error: unknown) => [401, 403, 404].includes(statusOf(error) ?? 0);

export function StoreFeedsPage() {
  const { user } = useSession(), { store } = useSelectedStore(), { feed } = useParams();
  return <FeedsWorkspace key={JSON.stringify([user.tenantId, user.id, user.role, store, feed])} selected={feed} allowed={canManageCatalog(user.role)} />;
}
function FeedsWorkspace({ selected, allowed }: { selected?: string; allowed: boolean }) {
  const { t } = useTranslation("feeds"), { store, storeRefreshing, storeRefreshError } = useSelectedStore();
  const read = useSetupProgress("feeds:" + store.id, signal => feedsApi.list(store.id, signal), () => false);
  const [categoriesOpen, setCategoriesOpen] = useState(false), [categoryPage, setCategoryPage] = useState(0);
  const categories = useSetupProgress(`feed-categories:${store.id}:${selected}:${categoriesOpen}`, signal => categoriesOpen && selected ? feedsApi.categories(store.id, selected, signal) : Promise.resolve(null), () => false);
  const [editor, setEditor] = useState<Editor | null>(null), [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<{ error: unknown; kind: Command["kind"]; uncertain: boolean } | null>(null), [success, setSuccess] = useState<Command["kind"] | null>(null), [invalidCategory, setInvalidCategory] = useState(false), [revision, setRevision] = useState(0);
  const active = useRef(false), lock = useRef(false), controller = useRef<AbortController | null>(null), heading = useRef<HTMLHeadingElement>(null), feedback = useRef<HTMLDivElement>(null), panel = useRef<HTMLDivElement>(null), opener = useRef<HTMLButtonElement | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (failure || success) feedback.current?.focus(); }, [failure, success]);
  useEffect(() => { if (editor) { const field = panel.current?.querySelector<HTMLElement>("input"); (field ?? panel.current)?.focus(); } }, [editor?.kind]); // eslint-disable-line react-hooks/exhaustive-deps
  const state = read.state, readError = state.status === "ready" ? state.refreshError : state.status === "loading" ? null : state.error;
  const feeds = state.status === "ready" && !denied(readError) ? state.data : null;
  const feed = feeds?.find(item => item.feed === selected);
  const fresh = feeds !== null && state.status === "ready" && !state.refreshing && readError === null && !read.suspended;
  const storeCurrent = !storeRefreshing && storeRefreshError === null;
  const categoryState = categories.state, categoryError = categoryState.status === "ready" ? categoryState.refreshError : categoryState.status === "loading" ? null : categoryState.error;
  const rows = categoryState.status === "ready" && !denied(categoryError) ? categoryState.data : null;
  const categoryFresh = rows !== null && categoryState.status === "ready" && !categoryState.refreshing && categoryError === null && !categories.suspended;
  const uncertain = failure?.uncertain === true;
  const blocked = !allowed || !fresh || !storeCurrent || pending || uncertain;
  const changed = editor !== null && (JSON.stringify(editor.before) !== JSON.stringify(feed) || editor.kind === "mapping" && JSON.stringify(editor.category) !== JSON.stringify(rows?.find(row => row.categoryId === editor.category.categoryId)));
  const editorBlocked = blocked || changed || editor?.kind === "mapping" && !categoryFresh;
  const safeCategoryPage = Math.min(categoryPage, Math.max(0, Math.ceil((rows?.length ?? 0) / 20) - 1));
  const root = `/stores/${encodeURIComponent(store.id)}`;
  function refresh() { if (lock.current) return; setRevision(value => value + 1); read.refresh(); if (categoriesOpen) categories.refresh(); }
  function close() {
    if (lock.current) return;
    setEditor(null); setInvalidCategory(false); if (!uncertain) setFailure(null);
    requestAnimationFrame(() => { if (active.current) { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); } });
  }
  function begin(next: Editor, button: HTMLButtonElement) {
    if (blocked || editor || !feeds || !read.isCurrent(feeds) || next.kind === "mapping" && (!rows || !categories.isCurrent(rows))) return;
    opener.current = button; setEditor(next); setFailure(null); setSuccess(null); setInvalidCategory(false);
  }
  async function execute(command: Command) {
    if (lock.current || blocked || !feeds || !feed || !read.isCurrent(feeds) || command.kind !== "run" && (!editor || editorBlocked) || command.kind === "run" && (editor !== null || !feed.isEnabled)) return;
    if (command.kind === "mapping" && (!rows || !categories.isCurrent(rows))) return;
    lock.current = true; setPending(true); setFailure(null); setSuccess(null); setRevision(value => value + 1); read.suspend(); if (categoriesOpen) categories.suspend();
    const request = new AbortController(); controller.current = request;
    try {
      if (command.kind === "mapping") {
        const saved = await feedsApi.mapCategory(store.id, feed.feed, command.categoryId, command.value, request.signal);
        if (!active.current || request.signal.aborted) return;
        categories.accept(rows!.map(row => row.categoryId === saved.categoryId ? saved : row));
      } else {
        const saved = command.kind === "settings" ? await feedsApi.save(store.id, feed.feed, command.enabled, request.signal)
          : command.kind === "rotate" ? await feedsApi.rotate(store.id, feed, request.signal) : await feedsApi.run(store.id, feed.feed, request.signal);
        if (!active.current || request.signal.aborted) return;
        read.accept(feeds.map(item => item.feed === saved.feed ? saved : item));
      }
      setEditor(null); setSuccess(command.kind);
    } catch (error) {
      if (!active.current || request.signal.aborted) return;
      const refused = isHttpError(error) && error.kind === "http" && [400, 401, 403, 404, 409, 429].includes(error.status ?? 0);
      setFailure({ error, kind: command.kind, uncertain: !refused });
    } finally {
      lock.current = false; controller.current = null;
      if (active.current && !request.signal.aborted) { setPending(false); read.resume(); if (categoriesOpen) categories.resume(); }
    }
  }
  function save(event: FormEvent) {
    event.preventDefault(); if (!editor || editorBlocked) return;
    if (editor.kind === "mapping") {
      if (editor.value.trim().length > 300) { setInvalidCategory(true); document.getElementById("feed-engine-category")?.focus(); return; }
      void execute({ kind: "mapping", categoryId: editor.category.categoryId, value: editor.value.trim() || null });
    } else void execute(editor.kind === "settings" ? { kind: "settings", enabled: editor.enabled } : { kind: "rotate" });
  }
  const detail = failure && isHttpError(failure.error) && typeof failure.error.problem?.detail === "string" ? failure.error.problem.detail : null;
  const fieldErrors = failure?.kind === "mapping" && isHttpError(failure.error) && Array.isArray(failure.error.problem?.errors?.engineCategory)
    ? [{ field: "feed-engine-category", message: failure.error.problem.errors.engineCategory.filter(value => typeof value === "string").join(" ") }] : [];
  return <div className="feeds-page">
    <Link to={selected ? `${root}/feeds` : root}>{t(selected ? "backFeeds" : "backStore")}</Link>
    <div className="feeds-heading"><h1 ref={heading} tabIndex={-1}>{feed ? feedName(feed.feed) : t("title")}</h1><Button type="button" variant="secondary" disabled={pending || read.refreshing} onClick={refresh}>{t("refresh")}</Button></div>
    <p>{t("scope", { store: store.name })}</p><p className="hint">{t("intro")}</p>
    {!allowed && <p>{t("readOnly")}</p>}
    {read.refreshing && <LoadingState label={t("loading")} lines={3} />}
    {readError !== null && <RequestError error={readError} operation="read" onRetry={refresh} />}
    {feeds && !fresh && !pending && <p role="status">{t("stale")}</p>}
    {!storeCurrent && <p role="status">{t("storeStale")}</p>}
    {feeds && !selected && <div className="feeds-grid">{feeds.length === 0 ? <p>{t("empty")}</p> : feeds.map(item => <section key={item.feed} className="feed-panel"><h2><Link to={`${root}/feeds/${encodeURIComponent(item.feed)}`}>{feedName(item.feed)}</Link></h2><FeedFacts feed={item} /></section>)}</div>}
    {feeds && selected && !feed && <p role="status">{t("missing")}</p>}
    {feed && <>
      <div ref={feedback} tabIndex={-1} className="feed-feedback">
        {success && <p role="status">{t(`success.${success}`)}</p>}
        {failure && <><RequestError error={failure.error} operation="write" fieldErrors={fieldErrors} />{detail && <p className="feed-literal">{detail}</p>}
          {uncertain && <><p>{t("uncertain")}</p><Button type="button" variant="secondary" disabled={!fresh || !storeCurrent || pending || failure.kind === "mapping" && !categoryFresh} onClick={() => { if (!feeds || !read.isCurrent(feeds) || failure.kind === "mapping" && (!rows || !categories.isCurrent(rows))) return; setFailure(null); setEditor(null); heading.current?.focus(); }}>{t("finishReview")}</Button></>}
        </>}
      </div>
      <div className="feeds-grid"><section className="feed-panel" aria-labelledby="feed-current-title"><h2 id="feed-current-title">{t("current")}</h2><FeedFacts feed={feed} />
        {allowed && <div className="cluster"><Button type="button" disabled={blocked || editor !== null} onClick={event => begin({ kind: "settings", before: feed, enabled: feed.isEnabled }, event.currentTarget)}>{t("editSettings")}</Button>
          <Button type="button" variant="secondary" disabled={blocked || editor !== null || !feed.isEnabled} busy={pending && !editor} busyLabel={t("running")} onClick={() => void execute({ kind: "run" })}>{t("run")}</Button></div>}
        <p className="hint">{t("runHint")}</p>
      </section><section className="feed-panel" aria-labelledby="feed-address-title"><h2 id="feed-address-title">{t("addressTitle")}</h2><p>{t("addressHint")}</p>
        {fresh && storeCurrent && !pending ? feed.url ? <FeedAddress key={feed.url} url={feed.url} /> : <p>{t("noAddress")}</p> : <p>{t("addressStale")}</p>}
        {allowed && <Button type="button" variant="danger" disabled={blocked || editor !== null || feed.url === null} onClick={event => begin({ kind: "rotate", before: feed }, event.currentTarget)}>{t("rotate")}</Button>}
      </section></div>
      {editor && <section className="feed-panel feed-editor" aria-label={t("activeChange")}><div ref={panel} tabIndex={-1} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); close(); } }}>
        <h2>{t(editor.kind === "settings" ? "editSettings" : editor.kind === "rotate" ? "rotate" : "editMapping")}</h2>
        <p>{t("target", { store: store.name, feed: feedName(feed.feed) })}</p>
        {changed && <p role="status">{t("changed")}</p>}
        <form noValidate onSubmit={save} aria-busy={pending || undefined}>
          {editor.kind === "settings" ? <><label className="feed-switch"><input type="checkbox" checked={editor.enabled} disabled={editorBlocked} onChange={event => setEditor({ ...editor, enabled: event.target.checked })} />{t("enable")}</label><p>{t("enableHint")}</p></>
            : editor.kind === "rotate" ? <p>{t("rotateWarning")}</p> : <><p><strong>{editor.category.name}</strong> · <code>{editor.category.categoryId}</code></p><Field id="feed-engine-category" name="engineCategory" label={t("engineCategory")} value={editor.value} maxLength={300} disabled={editorBlocked} hint={t("mappingInputHint")} error={invalidCategory ? t("categoryTooLong") : undefined} onChange={event => { setEditor({ ...editor, value: event.target.value }); setInvalidCategory(false); }} /></>}
          <div className="cluster"><Button type="submit" variant={editor.kind === "rotate" ? "danger" : "primary"} disabled={editorBlocked} busy={pending} busyLabel={t("saving")}>{t(editor.kind === "rotate" ? "confirmRotate" : "save")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancel")}</Button></div>
        </form>
      </div></section>}
      <FeedChecks key={JSON.stringify([feed, revision, fresh, storeCurrent])} storeId={store.id} feed={feed.feed} blocked={!fresh || !storeCurrent || pending} />
      <section className="feed-panel" aria-labelledby="feed-categories-title"><h2 id="feed-categories-title" tabIndex={-1}>{t("categories")}</h2><p>{t("mappingHint")}</p>
        {!categoriesOpen ? <Button type="button" variant="secondary" disabled={!fresh || !storeCurrent || pending} onClick={() => setCategoriesOpen(true)}>{t("loadCategories")}</Button> : <>
          {categories.refreshing && <LoadingState label={t("loadingCategories")} />}
          {categoryError !== null && <RequestError error={categoryError} operation="read" onRetry={categories.refresh} />}
          {rows && <>{!categoryFresh && <p role="status">{t("staleCategories")}</p>}{rows.length === 0 ? <p>{t("emptyCategories")}</p> : <>
            <ul className="feed-category-list">{rows.slice(safeCategoryPage * 20, (safeCategoryPage + 1) * 20).map(row => <li key={row.categoryId}><div><strong>{row.name}</strong><p className="feed-literal">{row.engineCategory ?? t("notMapped")}</p></div>
              {allowed && <Button type="button" variant="secondary" aria-label={t("editMappingNamed", { name: row.name })} disabled={blocked || !categoryFresh || editor !== null} onClick={event => begin({ kind: "mapping", before: feed, category: row, value: row.engineCategory ?? "" }, event.currentTarget)}>{t("editMapping")}</Button>}</li>)}</ul>
            {rows.length > 20 && <div className="cluster"><Button type="button" variant="secondary" disabled={safeCategoryPage === 0 || pending} onClick={() => { setCategoryPage(safeCategoryPage - 1); document.getElementById("feed-categories-title")?.focus(); }}>{t("previous")}</Button><span>{t("page", { page: safeCategoryPage + 1, total: Math.ceil(rows.length / 20) })}</span><Button type="button" variant="secondary" disabled={(safeCategoryPage + 1) * 20 >= rows.length || pending} onClick={() => { setCategoryPage(safeCategoryPage + 1); document.getElementById("feed-categories-title")?.focus(); }}>{t("next")}</Button></div>}
          </>}</>}
        </>}
      </section>
      <section className="feed-panel"><h2>{t("preparation")}</h2><ul><li>{t("eligibility")}</li><li>{t("identifiers")}</li><li>{t("shipping")}</li><li>{t("parameters")}</li></ul>
        <div className="cluster"><Link to={`${root}/products`}>{t("listingsLink")}</Link><Link to="/products">{t("physicalLink")}</Link><Link to={`${root}/attributes`}>{t("attributesLink")}</Link><Link to={`${root}/methods`}>{t("methodsLink")}</Link></div>
      </section>
    </>}
  </div>;
}
