import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { useSelectedStore } from "../adminContext";
import { metadataApi, metadataName, type MetadataKind, type MetadataSnapshot } from "../api/metadata";
import { statusOf } from "../api/errors";
import { isHttpError } from "../api/http";
import { contentParagraphs } from "../components/contentPageDraft";
import { metadataDraft, metadataHasImage, metadataInput, metadataIssues, metadataPreview, type MetadataDraft, type MetadataField } from "../components/metadataDraft";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useSetupProgress } from "../useSetupProgress";
import { ForbiddenPage } from "./RouteStatePage";
import "./MetadataPage.css";

export function MetadataPage({ kind }: { kind: MetadataKind }) {
  const { user } = useSession(), { store } = useSelectedStore(), params = useParams();
  if (!(kind === "store" || kind === "page" ? canManageStore(user.role) : canManageCatalog(user.role))) return <ForbiddenPage />;
  const id = kind === "store" ? store.id : params.listingId ?? params.categoryId ?? params.pageId ?? "";
  return <MetadataWorkspace key={`${user.tenantId}:${user.id}:${user.role}:${store.id}:${kind}:${id}`} kind={kind} id={id} />;
}
function backPath(storeId: string, kind: MetadataKind, id: string) {
  const root = `/stores/${storeId}`;
  return kind === "store" ? `${root}/settings` : `${root}/${kind === "listing" ? "products" : kind === "category" ? "categories" : "pages"}/${id}`;
}
function MetadataWorkspace({ kind, id }: { kind: MetadataKind; id: string }) {
  const { t } = useTranslation("metadata"), { store, storeRefreshing, storeRefreshError, reloadStores } = useSelectedStore();
  const read = useSetupProgress(`metadata:${store.id}:${kind}:${id}`, signal => metadataApi.read(store.id, kind, id, signal), () => false);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const [editor, setEditor] = useState<{ snapshot: MetadataSnapshot; draft: MetadataDraft; storeVersion: string } | null>(null);
  const [issues, setIssues] = useState<ReturnType<typeof metadataIssues>>([]), [saved, setSaved] = useState(false);
  const [failure, setFailure] = useState<{ error: unknown; uncertain: boolean } | null>(null);
  const mounted = useRef(false), controller = useRef<AbortController | null>(null), heading = useRef<HTMLHeadingElement>(null), summary = useRef<HTMLDivElement>(null), editButton = useRef<HTMLButtonElement>(null);
  const firstField = useRef<HTMLInputElement>(null);
  const outcome = useRef<HTMLDivElement>(null), editing = editor !== null;
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (editing) firstField.current?.focus(); }, [editing]);
  useEffect(() => { if (failure) outcome.current?.focus(); }, [failure]);
  const state = read.state, error = state.status === "ready" ? state.refreshError : state.status === "loading" ? null : state.error;
  const snapshot = state.status === "ready" && ![401, 403, 404].includes(statusOf(error) ?? 0) ? state.data : null;
  const fresh = snapshot !== null && state.status === "ready" && !state.refreshing && error === null && !read.suspended;
  const storeCurrent = !storeRefreshing && storeRefreshError === null;
  const retired = snapshot?.kind === "listing" && snapshot.item.archivedAt !== null;
  const changed = editor !== null && (JSON.stringify(editor.snapshot) !== JSON.stringify(snapshot) || editor.storeVersion !== JSON.stringify(store));
  const disabled = pending || !fresh || !storeCurrent || retired || failure?.uncertain === true;
  const draft = editor?.draft ?? (snapshot ? metadataDraft(snapshot) : null);
  const preview = snapshot && draft ? metadataPreview(snapshot, draft) : null;
  const fields: MetadataField[] = ["title", "description", ...(metadataHasImage(kind) ? ["socialImageUrl" as const] : []), ...(kind === "category" ? ["pageText" as const] : [])];
  const serverErrors = failure && isHttpError(failure.error) ? failure.error.problem?.errors : undefined;
  const serverName = (field: MetadataField) => `seo.${field === "title" && kind === "store" ? "titleSuffix" : field}`;
  const fieldMessage = (field: MetadataField) => {
    const issue = issues.find(issue => issue.field === field);
    if (issue) return t(issue.key);
    if (serverErrors && Object.hasOwn(serverErrors, serverName(field))) return t(field === "socialImageUrl" ? "imageInvalid" : field === "description" ? "descriptionInvalid" : field === "pageText" ? "pageTextInvalid" : "titleInvalid");
    return undefined;
  };
  const fieldErrors = fields.filter(field => fieldMessage(field)).map(field => ({ field: `metadata-${field}`, message: fieldMessage(field)! }));
  function update<K extends keyof MetadataDraft>(field: K, value: MetadataDraft[K]) {
    if (!editor) return;
    setEditor({ ...editor, draft: { ...editor.draft, [field]: value } }); setIssues(current => current.filter(issue => issue.field !== field));
    if (failure && !failure.uncertain) setFailure(null);
  }
  function close() {
    if (pending) return;
    setEditor(null); setIssues([]); if (!failure?.uncertain) setFailure(null);
    window.requestAnimationFrame(() => { if (mounted.current) { if (editButton.current && !editButton.current.disabled) editButton.current.focus(); else heading.current?.focus(); } });
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled || changed || !editor || !snapshot || !read.isCurrent(snapshot)) return;
    const next = metadataIssues(editor.draft, kind); setIssues(next);
    if (next.length) { window.requestAnimationFrame(() => summary.current?.focus()); return; }
    const target = editor;
    await run(async () => {
      read.suspend(); setSaved(false); setFailure(null); const request = new AbortController(); controller.current = request;
      try {
        const reply = await metadataApi.save(target.snapshot, metadataInput(target.draft, kind), request.signal);
        if (!mounted.current) return;
        read.accept(reply); setEditor(null); setSaved(true); heading.current?.focus();
      } catch (error) {
        if (!mounted.current) return;
        const refused = isHttpError(error) && error.kind === "http" && [400, 401, 403, 404, 409, 429].includes(error.status ?? 0);
        setFailure({ error, uncertain: !refused });
      } finally {
        controller.current = null;
        if (mounted.current) { read.resume(); if (kind === "store") reloadStores(); }
      }
    });
  }
  return <div className="metadata-page">
    <Link to={backPath(store.id, kind, id)}>{t("back")}</Link>
    <div className="metadata-heading"><h1 ref={heading} tabIndex={-1}>{t(kind === "store" ? "storeHeading" : "pageHeading")}</h1>
      <Button type="button" variant="secondary" disabled={pending || read.refreshing} onClick={read.refresh}>{t("refresh")}</Button></div>
    <p className="hint">{t("scope", { store: snapshot?.store.name ?? store.name })}</p>
    {read.refreshing && <LoadingState label={t("loading")} lines={3} />}
    {error !== null && <RequestError error={error} operation="read" onRetry={read.refresh} />}
    {!storeCurrent && <p role="status">{t("storeStale")}</p>}
    {!fresh && snapshot && !pending && <p role="status">{t("stale")}</p>}
    {statusOf(error) === 404 && <p>{t("missing")}</p>}
    {snapshot && draft && preview && <>
      <section className="metadata-panel"><h2 lang={snapshot.store.culture}>{metadataName(snapshot)}</h2><p className="hint">{t("intro")}</p><p className="hint">{t("fallbackHint")}</p>
        {kind === "store" && <p>{t("suffixHint")}</p>}{kind !== "store" && snapshot.store.seo.noIndex && <p>{t("storeNoIndex")}</p>}
        {retired && <p>{t("retired")}</p>}
        {saved && <p role="status" className="metadata-saved">{t("saved")}</p>}
        {!editor && <Button ref={editButton} type="button" disabled={disabled} onClick={() => {
          if (disabled || !read.isCurrent(snapshot)) return;
          setEditor({ snapshot: structuredClone(snapshot), draft: metadataDraft(snapshot), storeVersion: JSON.stringify(store) }); setSaved(false); setFailure(null);
        }}>{t("edit")}</Button>}
        {changed && <p role="status">{t("changed")}</p>}
        {editor && <form noValidate aria-label={t("edit")} aria-busy={pending || undefined} onSubmit={event => void save(event)} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); close(); } }}>
          {issues.length > 0 && <div className="validation-summary" ref={summary} tabIndex={-1} role="alert"><strong>{t("validationTitle")}</strong><ul>{issues.map(issue => <li key={issue.field}>
            <button type="button" className="link-button" onClick={() => document.getElementById(`metadata-${issue.field}`)?.focus()}>{t(issue.field === "title" && kind === "store" ? "titleSuffix" : issue.field)}: {t(issue.key)}</button>
          </li>)}</ul></div>}
          <fieldset disabled={disabled || changed} className="metadata-fields">
            <Field ref={firstField} id="metadata-title" name={serverName("title")} label={t(kind === "store" ? "titleSuffix" : "title")} value={draft.title} lang={snapshot.store.culture}
              hint={t("titleHint")} error={fieldMessage("title")} onChange={event => update("title", event.target.value)} />
            <Field id="metadata-description" name="seo.description" label={t("description")} value={draft.description} lang={snapshot.store.culture}
              hint={t("descriptionHint")} error={fieldMessage("description")} onChange={event => update("description", event.target.value)} />
            {metadataHasImage(kind) && <Field id="metadata-socialImageUrl" name="seo.socialImageUrl" label={t("socialImageUrl")} value={draft.socialImageUrl} inputMode="url" autoCapitalize="none" spellCheck={false}
              hint={t("imageHint")} error={fieldMessage("socialImageUrl")} onChange={event => update("socialImageUrl", event.target.value)} />}
            {kind !== "category" && <label className="metadata-check"><input name="seo.noIndex" type="checkbox" checked={draft.noIndex} onChange={event => update("noIndex", event.target.checked)} /><span>{t("noIndex")}</span></label>}
            {kind === "category" && <div className="metadata-text-field"><label htmlFor="metadata-pageText">{t("pageText")}</label><p className="hint" id="metadata-pageText-hint">{t("pageTextHint")}</p>
              <textarea id="metadata-pageText" name="seo.pageText" rows={8} lang={snapshot.store.culture} value={draft.pageText} aria-describedby={fieldMessage("pageText") ? "metadata-pageText-hint metadata-pageText-error" : "metadata-pageText-hint"} aria-invalid={!!fieldMessage("pageText") || undefined} onChange={event => update("pageText", event.target.value)} />
              {fieldMessage("pageText") && <p className="field-error" id="metadata-pageText-error">{fieldMessage("pageText")}</p>}
            </div>}
          </fieldset>
          <div className="cluster"><Button type="submit" disabled={disabled || changed} busy={pending} busyLabel={t("saving")}>{t("save")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancel")}</Button></div>
        </form>}
        {failure && <div className="metadata-outcome" ref={outcome} tabIndex={-1}><p role="status">{t(failure.uncertain ? "uncertain" : "refused")}</p><RequestError error={failure.error} operation="write" fieldErrors={editor ? fieldErrors : []} /></div>}
        {failure?.uncertain && <Button type="button" variant="secondary" disabled={!fresh || !storeCurrent || pending} onClick={() => {
          if (read.isCurrent(snapshot) && storeCurrent && !pending) { setFailure(null); heading.current?.focus(); }
        }}>{t("finishReview")}</Button>}
      </section>
      <section className="metadata-panel metadata-preview" aria-labelledby="metadata-preview-title"><h2 id="metadata-preview-title">{t("preview")}</h2><p className="hint">{t("previewHint")}</p>
        <dl><div><dt>{t("effectiveTitle")}</dt><dd lang={snapshot.store.culture}>{preview.title}</dd></div><div><dt>{t("description")}</dt><dd lang={snapshot.store.culture}>{preview.description ?? t("notSet")}</dd></div>
          <div><dt>{t("socialImageUrl")}</dt><dd>{preview.image === null ? t("notSet") : <code>{preview.image}</code>}</dd></div>
          <div><dt>{t("effectiveNoIndex")}</dt><dd>{t(preview.noIndex ? "requested" : "notRequested")}</dd></div></dl>
        {kind === "category" && <article className="metadata-page-text" lang={snapshot.store.culture} aria-label={t("pageText")}>
          {contentParagraphs(draft.pageText).map((paragraph, index) => <p key={index}>{paragraph}</p>)}{!draft.pageText.trim() && <p>{t("emptyText")}</p>}
        </article>}
      </section>
    </>}
  </div>;
}
