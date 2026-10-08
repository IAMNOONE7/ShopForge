import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { api, type AttributeDefinition, type Category } from "../api";
import { useAction } from "../useAction";
import type { RequestState } from "../useRequest";
import { CategoryAttributeForm } from "./CategoryAttributeForm";
import { CategoryForm } from "./CategoryForm";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

type Editor = { kind: "details" | "attributes"; category: Category; categories: Category[]; attributes: AttributeDefinition[] };

export function CategoryEditor({ storeId, category, categories, attributes, reloadAttributes, allowed, refreshing, refreshFailed, reload }: {
  storeId: string; category: Category; categories: Category[]; attributes: RequestState<AttributeDefinition[]>;
  reloadAttributes: () => void; allowed: boolean; refreshing: boolean; refreshFailed: boolean; reload: () => void;
}) {
  const { t } = useTranslation("categories");
  const [editor, setEditor] = useState<Editor | null>(null);
  const [saved, setSaved] = useState(false);
  const [attempted, setAttempted] = useState(false);
  const [error, run, pending] = useAction(reload);
  const root = useRef<HTMLDivElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const opener = useRef<HTMLButtonElement | null>(null);
  const attributesReady = attributes.status === "ready" && !attributes.refreshing && attributes.refreshError === null;
  const stale = editor !== null && (JSON.stringify(editor.categories) !== JSON.stringify(categories)
    || (editor.kind === "attributes" && attributes.status === "ready" && JSON.stringify(editor.attributes) !== JSON.stringify(attributes.data)));
  const disabled = !allowed || pending || refreshing || refreshFailed || stale;

  useEffect(() => {
    if (editor) (root.current?.querySelector<HTMLElement>("input") ?? root.current?.querySelector<HTMLElement>("h2"))?.focus();
  }, [editor]);

  function open(kind: Editor["kind"], button: HTMLButtonElement) {
    if (disabled || editor || (kind === "attributes" && !attributesReady)) return;
    opener.current = button; setSaved(false); setAttempted(false);
    setEditor({ kind, category, categories, attributes: attributes.status === "ready" ? attributes.data : [] });
  }
  function close() {
    setEditor(null); setAttempted(false);
    window.requestAnimationFrame(() => opener.current?.focus());
  }
  async function save(change: () => Promise<unknown>) {
    if (disabled || (editor?.kind === "attributes" && !attributesReady)) return;
    setAttempted(true); setSaved(false);
    await run(async () => {
      await change(); setEditor(null); setSaved(true);
      window.requestAnimationFrame(() => heading.current?.focus());
    });
  }

  return <div className="category-workspace">
    {saved && <InlineMessage tone="success">{t("saved")}</InlineMessage>}
    {(refreshing || refreshFailed) && <InlineMessage>{t(refreshFailed ? "refreshFailed" : "refreshing")}</InlineMessage>}
    {stale && <InlineMessage>{t("draftStale")}</InlineMessage>}
    {!allowed && <InlineMessage>{t("readOnly")}</InlineMessage>}
    <section className="physical-panel" aria-labelledby="category-details-title">
      <div className="category-section-heading"><h2 id="category-details-title" ref={heading} tabIndex={-1}>{t("details")}</h2>
        {allowed && <Button type="button" variant="secondary" disabled={disabled || !!editor} onClick={(event) => open("details", event.currentTarget)}>{t("editDetails")}</Button>}
      </div>
      <dl className="category-facts">
        <div><dt>{t("slug")}</dt><dd><code>{category.slug}</code></dd></div>
        <div><dt>{t("sortOrder")}</dt><dd>{category.sortOrder}</dd></div>
        <div><dt>{t("parent")}</dt><dd>{category.parentId
          ? categories.find((candidate) => candidate.id === category.parentId)?.name ?? t("unavailableParent", { id: category.parentId }) : t("root")}</dd></div>
      </dl>
    </section>
    <section className="physical-panel" aria-labelledby="category-attributes-title">
      <div className="category-section-heading"><h2 id="category-attributes-title">{t("assignedAttributes")}</h2>
        {allowed && <Button type="button" variant="secondary" disabled={disabled || !!editor || !attributesReady} onClick={(event) => open("attributes", event.currentTarget)}>{t("editAttributes")}</Button>}
      </div>
      <p className="hint">{t("attributesHint")}</p>
      {attributes.status === "loading" && <LoadingState label={t("loadingAttributes")} lines={2} />}
      {attributes.status !== "ready" && attributes.status !== "loading" && <RequestError error={attributes.error} operation="read" onRetry={reloadAttributes} />}
      {attributes.status === "ready" && <>
        {attributes.refreshError !== null && <RequestError error={attributes.refreshError} operation="read" onRetry={reloadAttributes} />}
        {category.attributeIds.length ? <ol className="category-assigned-list">{category.attributeIds.map((id) => {
          const attribute = attributes.data.find((candidate) => candidate.id === id);
          return <li key={id}><strong>{attribute?.name ?? t("unknownAttribute", { id })}</strong>{attribute && <span className="hint"> · {t(attribute.isFilterable ? "filterable" : "notFilterable")}</span>}</li>;
        })}</ol> : <p>{t("noAssignedAttributes")}</p>}
      </>}
    </section>
    {editor && <section className="physical-panel category-editor" ref={root}>
      {editor.kind === "details" ? <CategoryForm category={editor.category} categories={editor.categories} disabled={disabled} pending={pending} onCancel={close}
        onSave={(input) => save(() => api.updateCategory(storeId, category.id, input))} /> :
        <CategoryAttributeForm category={editor.category} attributes={editor.attributes} disabled={disabled || !attributesReady} pending={pending} onCancel={close}
          onSave={(ids) => save(() => api.assignCategoryAttributes(storeId, category.id, ids))} />}
      {attempted && error !== null && <RequestError error={error} operation="write" />}
    </section>}
  </div>;
}
