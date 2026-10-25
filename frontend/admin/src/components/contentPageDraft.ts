import type { ContentPage, ContentPageInput } from "../api/contentPages";
import { generatedCode, validCode } from "./attributeValidation";

export type ContentDraft = { title: string; slug: string; body: string };
export type ContentField = keyof ContentDraft;
export type ContentIssue = { field: ContentField; key: "titleInvalid" | "slugInvalid" | "slugTaken" | "bodyInvalid" };
export function contentDraft(page?: ContentPage): ContentDraft {
  return { title: page?.title ?? "", slug: page?.slug ?? "", body: page?.body ?? "" };
}
export function contentIssues(draft: ContentDraft, pages: ContentPage[], original?: ContentPage): ContentIssue[] {
  const issues: ContentIssue[] = [];
  if (!draft.title.trim() || draft.title.trim().length > 200) issues.push({ field: "title", key: "titleInvalid" });
  const slug = draft.slug.trim() || (original ? "" : generatedCode(draft.title));
  if (!validCode(slug)) issues.push({ field: "slug", key: "slugInvalid" });
  else if (pages.some(page => page.id !== original?.id && page.slug === slug)) issues.push({ field: "slug", key: "slugTaken" });
  if (draft.body.length > 20_000) issues.push({ field: "body", key: "bodyInvalid" });
  return issues;
}
export function contentInput(draft: ContentDraft, page?: ContentPage): ContentPageInput {
  return { title: draft.title.trim(), slug: draft.slug.trim() || null, body: draft.body,
    isPublished: page?.isPublished ?? false,
    seo: { title: page?.seoTitle ?? null, description: page?.seoDescription ?? null, noIndex: page?.seoNoIndex ?? false } };
}
export function contentParagraphs(body: string) {
  return body.split(/\n\s*\n/).map(paragraph => paragraph.trim()).filter(Boolean);
}
