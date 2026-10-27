import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { api, type AttributeDefinition } from "../api";
import { isHttpError } from "../api/http";
import { useRequest } from "../useRequest";
import { EditAttributeForm } from "./AttributeForms";
import { AttributeOptions, AttributeOptionEditor, type AttributeEditor } from "./AttributeOptions";
import { changeAttribute, type AttributeCommand } from "./attributeCommands";
import { Button } from "./ui/Button";
import { EmptyState } from "./ui/EmptyState";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";
import "./AttributeWorkspace.css";

export function AttributeFacts({ definition }: { definition: AttributeDefinition }) {
  const { t } = useTranslation("attributes");
  return <dl className="attribute-readonly">
    <div><dt>{t("code")}</dt><dd><code>{definition.code}</code></dd></div>
    <div><dt>{t("type")}</dt><dd>{t(`types.${definition.type}`)}</dd></div>
    <div><dt>{t("unitOptional")}</dt><dd>{definition.unit || "—"}</dd></div>
    {(["isFilterable", "isVisibleOnProductPage", "isInFeeds", "isSearchable"] as const).map((key, index) => <div key={key}><dt>{t((["filter", "shown", "inFeeds", "searchable"] as const)[index])}</dt><dd>{t(definition[key] ? "yes" : "no")}</dd></div>)}
    <div><dt>{t("sortOrder")}</dt><dd>{definition.sortOrder}</dd></div>
  </dl>;
}

export function AttributeWorkspace({ storeId, storeName, attributeId, allowed, storeStale }: {
  storeId: string; storeName: string; attributeId: string; allowed: boolean; storeStale: boolean;
}) {
  const { t } = useTranslation("attributes");
  const navigate = useNavigate();
  const [state, reload] = useRequest(`attributes:${storeId}`, signal => api.attributes(storeId, signal));
  const ready = state.status === "ready" ? state : null;
  const denied = isHttpError(ready?.refreshError) && [401, 403, 404].includes(ready.refreshError.status ?? 0);
  const definition = !denied ? ready?.data.find(item => item.id === attributeId) : undefined;
  const [editor, setEditor] = useState<AttributeEditor | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<unknown | null>(null);
  const [success, setSuccess] = useState<AttributeCommand["kind"] | null>(null);
  const [needsReview, setNeedsReview] = useState(false);
  const [writeSnapshot, setWriteSnapshot] = useState<AttributeDefinition[] | null>(null);
  const active = useRef(true), lock = useRef(false), controller = useRef<AbortController | null>(null);
  const opener = useRef<HTMLButtonElement | null>(null), result = useRef<HTMLDivElement>(null), title = useRef<HTMLHeadingElement>(null), confirmation = useRef<HTMLDivElement>(null);
  const current = useRef({ allowed, fresh: false, signature: "" });
  const readFresh = Boolean(ready && !ready.refreshing && ready.refreshError === null && ready.data !== writeSnapshot && !storeStale);
  const fresh = readFresh && Boolean(definition);
  const signature = JSON.stringify(definition);
  const staleEditor = editor !== null && JSON.stringify(editor.baseline) !== signature;
  const disabled = pending || !fresh || needsReview;
  useLayoutEffect(() => { current.current = { allowed, fresh: fresh && !needsReview, signature }; }, [allowed, fresh, needsReview, signature]);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (error !== null || success !== null) result.current?.focus(); }, [error, success]);
  useEffect(() => { if (editor?.kind.startsWith("remove")) confirmation.current?.focus(); }, [editor]);
  useEffect(() => {
    if (!editor || pending) return;
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") { event.preventDefault(); close(); } };
    document.addEventListener("keydown", escape); return () => document.removeEventListener("keydown", escape);
  }, [editor, pending]); // eslint-disable-line react-hooks/exhaustive-deps

  function close() {
    if (lock.current) return;
    setEditor(null); setError(null);
    requestAnimationFrame(() => { if (!active.current) return; if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else title.current?.focus(); });
  }
  function begin(kind: AttributeEditor["kind"], button: HTMLButtonElement, option?: AttributeDefinition["options"][number]) {
    if (!allowed || disabled || editor || !definition) return;
    opener.current = button; setSuccess(null); setError(null); setEditor({ kind, baseline: definition, option });
  }
  async function execute(command: AttributeCommand) {
    if (lock.current || !editor || !ready || !current.current.allowed || !current.current.fresh || JSON.stringify(editor.baseline) !== current.current.signature) return;
    lock.current = true; setPending(true); setError(null); setSuccess(null);
    const request = new AbortController(); controller.current = request;
    try {
      await changeAttribute(storeId, editor.baseline, command, request.signal);
      if (!active.current || request.signal.aborted) return;
      if (command.kind === "removeDefinition") { void navigate(`/stores/${encodeURIComponent(storeId)}/attributes`, { replace: true }); return; }
      setSuccess(command.kind); setEditor(null); setWriteSnapshot(ready.data); reload();
    } catch (failure) {
      if (!active.current || request.signal.aborted) return;
      setError(failure);
      if (!isHttpError(failure) || failure.kind !== "http" || failure.status === null || failure.status >= 500 || failure.status === 404) {
        setNeedsReview(true); setWriteSnapshot(ready.data); reload();
      }
    } finally { lock.current = false; if (active.current) setPending(false); }
  }
  const detail = isHttpError(error) && typeof error.problem?.detail === "string" ? error.problem.detail : null;
  const fieldErrors = isHttpError(error) && error.status === 400 && error.problem?.errors
    ? Object.entries(error.problem.errors).flatMap(([field, messages]) => {
      if (!Array.isArray(messages)) return [];
      const message = messages.filter(value => typeof value === "string").join(" ");
      return message ? [{ field: editor && ["add", "rename"].includes(editor.kind) && ["name", "code"].includes(field)
        ? "attribute-option-name" : field, message }] : [];
    }) : undefined;
  return <div className="attribute-detail-page">
    <Link to={`/stores/${storeId}/attributes`} className="physical-back-link">{t("backToAttributes")}</Link>
    <div className="page-heading-row"><h1 ref={title} tabIndex={-1}>{definition?.name ?? t("detailTitle")}</h1>
      <Button type="button" variant="secondary" disabled={pending || state.status === "loading" || ready?.refreshing} onClick={reload}>{t("refresh")}</Button></div>
    {state.status === "loading" && <LoadingState label={t("loading")} />}
    {(state.status === "error" || state.status === "not-found") && <RequestError error={state.error} operation="read" onRetry={reload} />}
    {ready?.refreshError !== null && ready?.refreshError !== undefined && <RequestError error={ready.refreshError} operation="read" onRetry={reload} />}
    {ready && !readFresh && !denied && <p role="status" className="hint">{t("staleRead")}</p>}
    {ready && !definition && !denied && <EmptyState headingLevel={2} title={t("missingTitle")}><p>{t("missingBody")}</p></EmptyState>}
    {definition && <>
      <div ref={result} tabIndex={-1} className="attribute-feedback">
        {error !== null && <><RequestError error={error} operation="write" fieldErrors={fieldErrors} />{detail && <p className="attribute-server-detail">{detail}</p>}
          {isHttpError(error) && error.status === 409 && editor?.kind.startsWith("remove") && <div className="cluster">
            <Link to={`/stores/${encodeURIComponent(storeId)}/products`}>{t("reviewListings")}</Link>
            {editor.kind === "removeDefinition" && <Link to={`/stores/${encodeURIComponent(storeId)}/categories`}>{t("reviewCategories")}</Link>}
          </div>}</>}
        {success && <InlineMessage tone="success">{t(`outcomes.${success}`)}</InlineMessage>}
        {needsReview && <InlineMessage tone="error"><p>{t("uncertainWrite")}</p>
          <Button type="button" disabled={!fresh || pending} onClick={() => { setNeedsReview(false); setError(null); setEditor(null); requestAnimationFrame(() => title.current?.focus()); }}>{t("reviewCurrent")}</Button></InlineMessage>}
      </div>
      <div className="attribute-detail-grid">
        <section className="physical-panel attribute-definition-panel"><h2>{t("editTitle")}</h2><p className="hint">{t("editHint")}</p>
          <AttributeFacts definition={definition} />
          {allowed && <Button type="button" disabled={disabled || editor !== null} onClick={event => begin("update", event.currentTarget)}>{t("editDefinition")}</Button>}
        </section>
        <AttributeOptions definition={definition} allowed={allowed} disabled={disabled} editing={editor !== null} begin={begin} />
      </div>
      {editor && allowed && <section className="physical-panel attribute-active-editor" aria-label={t("currentEdit")}>
        {staleEditor && <InlineMessage tone="error">{t("staleDraft")}</InlineMessage>}
        {editor.kind === "update" ? <EditAttributeForm definition={editor.baseline} pending={pending} disabled={disabled || staleEditor} onSave={input => execute({ kind: "update", input })} onCancel={close} />
          : editor.kind.startsWith("remove") ? <div ref={confirmation} role="group" aria-labelledby="attribute-remove-title" tabIndex={-1}>
            <h2 id="attribute-remove-title">{t(editor.kind === "removeDefinition" ? "removeDefinition" : "removeOption")}</h2>
            <p>{t("confirmTarget", { store: storeName, attribute: editor.baseline.name, code: editor.baseline.code })}</p>
            {editor.option && <p><strong>{editor.option.name}</strong> · <code>{editor.option.code}</code></p>}
            <p>{t(editor.kind === "removeDefinition" ? "deleteDefinitionHint" : "deleteOptionHint")}</p>
            <div className="cluster"><Button type="button" variant="danger" disabled={disabled || staleEditor} onClick={() => void execute(editor.kind === "removeDefinition" ? { kind: "removeDefinition" } : { kind: "removeOption", optionId: editor.option!.id })}>{t("confirmRemove")}</Button>
              <Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancelEdit")}</Button></div>
          </div> : <AttributeOptionEditor editor={editor} pending={pending} disabled={disabled || staleEditor} onSave={execute} onCancel={close} />}
      </section>}
      {allowed && <section className="physical-panel attribute-delete-panel"><h2>{t("removeDefinition")}</h2><p>{t("deleteDefinitionHint")}</p>
        <div className="cluster"><Link to={`/stores/${storeId}/products`}>{t("reviewListings")}</Link><Link to={`/stores/${storeId}/categories`}>{t("reviewCategories")}</Link></div>
        <Button type="button" variant="danger" disabled={disabled || editor !== null} onClick={event => begin("removeDefinition", event.currentTarget)}>{t("removeDefinition")}</Button>
      </section>}
    </>}
  </div>;
}
