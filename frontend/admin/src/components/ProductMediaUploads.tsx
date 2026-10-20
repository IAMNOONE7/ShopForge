import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api } from "../api";
import { isHttpError } from "../api/http";
import { uncertainMediaWrite, type useProductMedia } from "../useProductMedia";
import { validateProductImage } from "./productValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

const maxFiles = 10;
type Row = { id: number; file: File; altText: string; preview: string | null; status: "ready" | "uploading" | "uploaded" | "failed" | "uncertain" | "invalid"; error: unknown | null };

export function ProductMediaUploads({ productId, media, disabled, onQueueChange, reloadProducts }: {
  productId: string; media: ReturnType<typeof useProductMedia>; disabled: boolean; onQueueChange: (open: boolean) => void; reloadProducts: () => void;
}) {
  const { t } = useTranslation("products");
  const [rows, setRows] = useState<Row[]>([]), [running, setRunning] = useState(false), [stopping, setStopping] = useState(false);
  const [tooMany, setTooMany] = useState(false), [reviewed, setReviewed] = useState<string | null>(null);
  const active = useRef(false), executing = useRef(false), stop = useRef(false), serial = useRef(0);
  const previews = useRef<string[]>([]), input = useRef<HTMLInputElement>(null);
  const revision = JSON.stringify(media.images), uncertain = rows.some(row => row.status === "uncertain");
  useEffect(() => { active.current = true; return () => { active.current = false; stop.current = true; previews.current.forEach(url => URL.revokeObjectURL(url)); }; }, []);
  useEffect(() => { onQueueChange(rows.length > 0); }, [rows.length, onQueueChange]);
  function choose(files: FileList | null) {
    if (disabled || !media.canWrite || rows.length || executing.current) return;
    const chosen = [...(files ?? [])]; setTooMany(chosen.length > maxFiles);
    if (chosen.length > maxFiles) { if (input.current) input.current.value = ""; return; }
    setRows(chosen.map(file => {
      const valid = validateProductImage(file, "").length === 0;
      const preview = valid ? URL.createObjectURL(file) : null;
      if (preview) previews.current.push(preview);
      return { id: ++serial.current, file, altText: "", preview, status: valid ? "ready" : "invalid", error: null };
    }));
  }
  function update(id: number, patch: Partial<Row>) { setRows(current => current.map(row => row.id === id ? { ...row, ...patch } : row)); }
  function clear() {
    if (executing.current || media.busy || (uncertain && (!media.canWrite || reviewed !== revision))) return;
    previews.current.forEach(url => URL.revokeObjectURL(url)); previews.current = [];
    setRows([]); setReviewed(null); setTooMany(false);
    if (input.current) input.current.value = "";
    window.requestAnimationFrame(() => input.current?.focus());
  }
  async function start(event: FormEvent) {
    event.preventDefault();
    if (disabled || executing.current || !media.canWrite) return;
    const ready = rows.filter(row => row.status === "ready");
    if (!ready.length || ready.some(row => validateProductImage(row.file, row.altText).length > 0)) return;
    executing.current = true; stop.current = false; setRunning(true); setStopping(false); setReviewed(null);
    try {
      for (const row of ready) {
        if (!active.current || stop.current) break;
        update(row.id, { status: "uploading" });
        const result = await media.change(async () => {
          const image = await api.uploadProductImage(productId, row.file, row.altText);
          media.checkNewImage(image);
        });
        if (!active.current) return;
        if (result.blocked) { update(row.id, { status: "ready" }); break; }
        update(row.id, { status: result.acknowledged ? "uploaded" : uncertainMediaWrite(result.error) ? "uncertain" : "failed", error: result.error });
        if (!result.acknowledged || !result.fresh) break;
      }
    } finally {
      executing.current = false;
      if (active.current) { setRunning(false); setStopping(false); reloadProducts(); }
    }
  }
  const ready = rows.filter(row => row.status === "ready"), uploaded = rows.filter(row => row.status === "uploaded").length;
  return <form className="product-media-form" aria-labelledby="media-upload-title" noValidate onSubmit={start}>
    <h3 id="media-upload-title">{t("mediaUploadTitle")}</h3><p className="hint">{t("mediaQueueHint", { count: maxFiles })}</p>
    <div className="product-file-field"><label htmlFor="media-files">{t("chooseImage")}</label>
      <input ref={input} id="media-files" name="file" type="file" multiple accept="image/jpeg,image/png,image/webp" disabled={disabled || !media.canWrite || rows.length > 0 || running}
        aria-describedby="media-file-hint" onChange={event => choose(event.target.files)} />
      <span id="media-file-hint" className="hint">{t("imageRequirements")}</span>
    </div>
    {tooMany && <InlineMessage tone="error">{t("mediaTooMany", { count: maxFiles })}</InlineMessage>}
    {rows.length > 0 && <>
      <p role="status" aria-live="polite">{t("mediaQueueProgress", { uploaded, total: rows.length })}</p>
      <ol className="media-upload-queue">{rows.map(row => <li key={row.id}>
        <strong>{row.file.name}</strong><span>{t(`mediaQueueStates.${row.status}`)}</span>
        {row.preview && <div className="product-selected-preview"><img src={row.preview} alt={t("mediaFilePreview", { name: row.file.name })} /></div>}
        {row.status === "invalid" ? <ul>{validateProductImage(row.file, row.altText).map(issue => <li key={issue.key}>{t(`validation.${issue.key}`)}</li>)}</ul> : <Field
          id={`upload-alt-${row.id}`} name={`upload-alt-${row.id}`} label={t("mediaFileAlt", { name: row.file.name })} hint={t("mediaAltHint")} maxLength={200}
          value={row.altText} readOnly={row.status !== "ready" || running} disabled={disabled}
          error={row.altText.trim().length > 200 ? t("validation.altTextInvalid") : undefined} onChange={event => { if (row.status === "ready" && !executing.current) update(row.id, { altText: event.target.value }); }} />}
        {row.error !== null && <><RequestError error={row.error} operation="write" fieldErrors={[]} />
          <p>{t(row.status === "uncertain" ? "mediaUploadUncertain" : "mediaUploadRefused")}</p>
          {isHttpError(row.error) && row.error.status === 429 && <p>{t("mediaRateLimited")}{row.error.retryAfter && /^\d+$/.test(row.error.retryAfter) && Number.isSafeInteger(Number(row.error.retryAfter)) ? ` ${t("mediaWaitSeconds", { count: Number(row.error.retryAfter) })}` : ""}</p>}
        </>}
      </li>)}</ol>
      {uncertain && <div className="media-upload-review"><p>{t("mediaReviewHint")}</p>
        <label><input type="checkbox" checked={reviewed === revision} disabled={running || !media.canWrite} onChange={event => setReviewed(event.target.checked ? revision : null)} />{t("mediaReviewed")}</label>
      </div>}
      <div className="cluster">
        <Button type="submit" disabled={disabled || running || !media.canWrite || ready.length === 0 || ready.some(row => row.altText.trim().length > 200)}>{t("mediaStart")}</Button>
        {running && <Button type="button" variant="secondary" disabled={stopping} onClick={() => { stop.current = true; setStopping(true); }}>{t(stopping ? "mediaStopping" : "mediaStop")}</Button>}
        <Button type="button" variant="secondary" disabled={running || media.busy || uncertain && (!media.canWrite || reviewed !== revision)} onClick={clear}>{t("mediaClear")}</Button>
      </div>
      {stopping && <p role="status">{t("mediaStopHint")}</p>}
    </>}
  </form>;
}
