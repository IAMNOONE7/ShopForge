import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { api, type ProductImage } from "../api";
import { isHttpError } from "../api/http";
import { uncertainMediaWrite, useProductMedia } from "../useProductMedia";
import { ProductMediaUploads } from "./ProductMediaUploads";
import { ProductMediaEditor } from "./ProductMediaEditor";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

export type MediaEditor = { kind: "order" | "description" | "remove"; images: ProductImage[]; imageId?: string; snapshot: string };
type Props = { productId: string; sku: string; images: ProductImage[]; canManage: boolean; refreshing: boolean; refreshFailed: boolean; reloadProducts: () => void };

export function ProductMediaSection(props: Props) { return <ProductMediaWorkspace key={props.productId} {...props} />; }

function ProductMediaWorkspace(props: Props) {
  const { t } = useTranslation("products"); const media = useProductMedia(props);
  const [editor, setEditor] = useState<MediaEditor | null>(null), [queueOpen, setQueueOpen] = useState(false);
  const [error, setError] = useState<unknown>(null), [saved, setSaved] = useState(false);
  const active = useRef(false), heading = useRef<HTMLHeadingElement>(null), opener = useRef<HTMLButtonElement | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const snapshot = JSON.stringify(media.images), stale = editor !== null && editor.snapshot !== snapshot;
  const disabled = !media.canWrite || queueOpen || stale;
  async function refresh() { if (media.busy) return; await media.refresh(); if (active.current) props.reloadProducts(); }
  function open(kind: MediaEditor["kind"], button: HTMLButtonElement, imageId?: string) {
    if (disabled || editor) return;
    opener.current = button; setError(null); setSaved(false); setEditor({ kind, images: media.images, snapshot, imageId });
  }
  function close() {
    if (media.busy) return;
    setEditor(null); setError(null);
    window.requestAnimationFrame(() => { if (opener.current?.isConnected) opener.current.focus(); else heading.current?.focus(); });
  }
  async function save(write: () => Promise<unknown>) {
    if (disabled || !editor) return;
    setError(null); setSaved(false);
    const result = await media.change(write);
    if (!active.current || result.blocked) return;
    setError(result.error);
    if (result.acknowledged) { setEditor(null); setSaved(true); heading.current?.focus(); }
    props.reloadProducts();
  }
  return <section className="physical-panel product-media-workspace" aria-labelledby="physical-media-title">
    <div className="section-heading"><h2 id="physical-media-title" ref={heading} tabIndex={-1}>{t("images")}</h2>
      <Button type="button" variant="secondary" data-read-action disabled={media.busy} onClick={() => void refresh()}>{t("mediaRefresh")}</Button></div>
    <p className="hint">{t("mediaFirstHint")}</p>
    {(props.refreshing || props.refreshFailed || media.needsRead) && <InlineMessage>{t("mediaReadRequired")}</InlineMessage>}
    {media.readError !== null && <RequestError error={media.readError} operation="read" onRetry={() => void refresh()} />}
    {saved && <InlineMessage tone="success">{t("mediaSaved")}</InlineMessage>}
    {media.images.length ? <>
      <ol className="physical-image-grid">{media.images.map((image, index) => <li key={image.id}>
        <div className="physical-image-frame"><img src={image.url} alt={image.altText || t("imageFallbackAlt", { sku: props.sku, number: index + 1 })} loading="lazy" /></div>
        <strong>{t(index === 0 ? "mediaFirst" : "imageNumber", { number: index + 1 })}</strong><p>{image.altText || t("noAltText")}</p>
        {props.canManage && <div className="media-image-actions">
          <Button type="button" variant="secondary" disabled={disabled || editor !== null} onClick={event => open("description", event.currentTarget, image.id)}>{t("mediaEditAlt", { number: index + 1 })}</Button>
          <Button type="button" variant="secondary" disabled={disabled || editor !== null} onClick={event => open("remove", event.currentTarget, image.id)}>{t("removeImageNamed", { name: image.altText || t("imageNumber", { number: index + 1 }) })}</Button>
        </div>}
      </li>)}</ol>
      {props.canManage && <Button type="button" variant="secondary" disabled={disabled || editor !== null || media.images.length < 2} onClick={event => open("order", event.currentTarget)}>{t("mediaOrder")}</Button>}
    </> : !media.needsRead && <div className="physical-image-placeholder" role="status">{t("noImages")}</div>}
    {editor && <>
      {stale && <InlineMessage>{t("mediaDraftStale")}</InlineMessage>}
      <ProductMediaEditor editor={editor} disabled={disabled} pending={media.busy} onCancel={close}
        onOrder={ids => save(() => api.reorderProductImages(props.productId, ids))}
        onDescription={(id, text) => save(() => api.describeProductImage(props.productId, id, text))}
        onRemove={id => save(() => api.deleteProductImage(props.productId, id))} />
      {error !== null && <><RequestError error={error} operation="write" />
        <InlineMessage>{t(uncertainMediaWrite(error) ? "mediaUncertain" : "mediaRefused")}</InlineMessage>
        {isHttpError(error) && error.status === 429 && <p>{t("mediaRateLimited")}</p>}
      </>}
    </>}
    {props.canManage && <ProductMediaUploads productId={props.productId} media={media} disabled={editor !== null}
      onQueueChange={setQueueOpen} reloadProducts={props.reloadProducts} />}
  </section>;
}
