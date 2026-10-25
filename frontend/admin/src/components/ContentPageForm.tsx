import { useDeferredValue, useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { ContentPage, ContentPageInput } from "../api/contentPages";
import { generatedCode } from "./attributeValidation";
import { contentDraft, contentInput, contentIssues, contentParagraphs, type ContentField, type ContentIssue } from "./contentPageDraft";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export function ContentPageForm({ page, pages, disabled, pending, culture, onSave, onCancel, onEdit }: {
  page?: ContentPage; pages: ContentPage[]; disabled: boolean; pending: boolean; culture: string;
  onSave: (input: ContentPageInput) => Promise<void>; onCancel?: () => void; onEdit: () => void;
}) {
  const { t } = useTranslation("contentPages");
  const [draft, setDraft] = useState(() => contentDraft(page)), [issues, setIssues] = useState<ContentIssue[]>([]);
  const [preview, setPreview] = useState(false), body = useDeferredValue(draft.body);
  const summary = useRef<HTMLDivElement>(null);
  const title = useRef<HTMLInputElement>(null);
  useEffect(() => { title.current?.focus(); }, []);
  function update(field: ContentField, value: string) {
    setDraft(current => ({ ...current, [field]: value }));
    setIssues(current => current.filter(issue => issue.field !== field && !(field === "title" && !draft.slug.trim() && issue.field === "slug")));
    onEdit();
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled) return;
    const next = contentIssues(draft, pages, page); setIssues(next);
    if (next.length) { window.requestAnimationFrame(() => summary.current?.focus()); return; }
    await onSave(contentInput(draft, page));
  }
  const message = (field: ContentField) => { const issue = issues.find(issue => issue.field === field); return issue ? t(issue.key) : undefined; };
  return <section className="content-panel content-editor" aria-labelledby="content-editor-title">
    <h2 id="content-editor-title">{t(page ? "editTitle" : "createTitle")}</h2>
    {page?.isPublished && <p className="content-important">{t("publishedEdit")}</p>}
    <p className="hint">{t("languageHint")}</p>
    <form noValidate aria-labelledby="content-editor-title" onSubmit={event => void submit(event)} onKeyDown={event => {
      if (event.key === "Escape" && !pending && onCancel) { event.preventDefault(); onCancel(); }
    }}>
      {issues.length > 0 && <div className="validation-summary" ref={summary} role="alert" tabIndex={-1}><strong>{t("validationTitle")}</strong>
        <ul>{issues.map(issue => <li key={issue.field}><button type="button" className="link-button" onClick={() => document.getElementById(`content-${issue.field}`)?.focus()}>
          {t(issue.field)}: {t(issue.key)}</button></li>)}</ul>
      </div>}
      <fieldset disabled={disabled} className="content-fields">
        <Field ref={title} id="content-title" name="title" label={t("title")} hint={t("titleHint")} value={draft.title} required lang={culture}
          error={message("title")} onChange={event => update("title", event.target.value)} />
        <Field id="content-slug" name="slug" label={t("slug")} hint={t(page ? "slugEditHint" : "slugCreateHint")} value={draft.slug}
          error={message("slug")} required={!!page} autoCapitalize="none" autoCorrect="off" spellCheck={false} onChange={event => update("slug", event.target.value)} />
        <p className="content-path"><span>{t("path")}</span> <code>/pages/{draft.slug.trim() || (!page && generatedCode(draft.title)) || "…"}</code></p>
        {page && draft.slug.trim() !== page.slug && <p className="content-important">{t("slugWarning")}</p>}
        <div className="content-body-field"><label htmlFor="content-body">{t("body")}</label>
          <p id="content-body-hint" className="hint">{t("bodyHint")}</p>
          <textarea id="content-body" name="body" rows={12} value={draft.body} lang={culture} aria-describedby={`content-body-hint content-body-count${message("body") ? " content-body-error" : ""}`}
            aria-invalid={!!message("body") || undefined} onChange={event => update("body", event.target.value)} />
          <p id="content-body-count" className="hint">{t("bodyCount", { count: draft.body.length, max: 20_000 })}</p>
          {message("body") && <p id="content-body-error" className="field-error">{message("body")}</p>}
        </div>
      </fieldset>
      <div className="cluster"><Button type="submit" disabled={disabled} busy={pending} busyLabel={t("saving")}>{t(page ? "save" : "create")}</Button>
        {onCancel && <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancelEdits")}</Button>}</div>
    </form>
    <Button type="button" variant="quiet" aria-expanded={preview} aria-controls="content-text-preview" onClick={() => setPreview(value => !value)}>{t(preview ? "hidePreview" : "preview")}</Button>
    {preview && <article id="content-text-preview" className="content-text-preview" lang={culture} aria-label={t("preview")}>
      <h3>{draft.title || t("untitled")}</h3>{contentParagraphs(body).map((paragraph, index) => <p key={index}>{paragraph}</p>)}
      {!body.trim() && <p>{t("emptyBody")}</p>}
    </article>}
  </section>;
}
