import { metadataName, metadataValues, type MetadataKind, type MetadataSnapshot, type MetadataValues } from "../api/metadata";

export type MetadataDraft = { title: string; description: string; socialImageUrl: string; noIndex: boolean; pageText: string };
export type MetadataField = Exclude<keyof MetadataDraft, "noIndex">;
export type MetadataIssue = { field: MetadataField; key: "titleInvalid" | "descriptionInvalid" | "imageInvalid" };
export const metadataHasImage = (kind: MetadataKind) => kind === "store" || kind === "listing";
export function metadataDraft(snapshot: MetadataSnapshot): MetadataDraft {
  const values = metadataValues(snapshot);
  return { title: values.title ?? "", description: values.description ?? "", socialImageUrl: values.socialImageUrl ?? "", noIndex: values.noIndex, pageText: values.pageText ?? "" };
}
export function metadataIssues(draft: MetadataDraft, kind: MetadataKind): MetadataIssue[] {
  const issues: MetadataIssue[] = [];
  if (draft.title.trim().length > 200) issues.push({ field: "title", key: "titleInvalid" });
  if (draft.description.trim().length > 500) issues.push({ field: "description", key: "descriptionInvalid" });
  const image = draft.socialImageUrl.trim();
  if (metadataHasImage(kind) && image) {
    let valid = false;
    try { const url = new URL(image); valid = /^https?:\/\//i.test(image) && ["http:", "https:"].includes(url.protocol) && !!url.hostname && image.length <= 2000; } catch { /* A non-address is reported with the field. */ }
    if (!valid) issues.push({ field: "socialImageUrl", key: "imageInvalid" });
  }
  return issues;
}
export function metadataInput(draft: MetadataDraft, kind: MetadataKind): MetadataValues {
  return { title: draft.title.trim() || null, description: draft.description.trim() || null,
    socialImageUrl: metadataHasImage(kind) ? draft.socialImageUrl.trim() || null : null,
    noIndex: kind !== "category" && draft.noIndex, pageText: kind === "category" ? draft.pageText.trim() || null : null };
}
export function metadataPreview(snapshot: MetadataSnapshot, draft: MetadataDraft) {
  const values = metadataInput(draft, snapshot.kind), defaults = snapshot.store.seo;
  const suffix = snapshot.kind === "store" ? values.title : defaults.titleSuffix;
  const name = metadataName(snapshot);
  return {
    title: snapshot.kind !== "store" && values.title ? values.title : suffix ? `${name} — ${suffix}` : name,
    description: snapshot.kind === "store" ? values.description : values.description ?? defaults.description,
    image: snapshot.kind === "store" ? values.socialImageUrl : values.socialImageUrl ?? defaults.socialImageUrl,
    noIndex: snapshot.kind === "store" ? values.noIndex : defaults.noIndex || values.noIndex,
  };
}
