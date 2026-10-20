import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { MediaEditor } from "./ProductMediaSection";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export function ProductMediaEditor({ editor, disabled, pending, onCancel, onOrder, onDescription, onRemove }: {
  editor: MediaEditor; disabled: boolean; pending: boolean; onCancel: () => void;
  onOrder: (ids: string[]) => Promise<void>; onDescription: (id: string, text: string) => Promise<void>; onRemove: (id: string) => Promise<void>;
}) {
  const { t } = useTranslation("products"); const [ids, setIds] = useState(editor.images.map(image => image.id));
  const selected = editor.images.find(image => image.id === editor.imageId), [text, setText] = useState(selected?.altText ?? "");
  const root = useRef<HTMLFormElement>(null);
  useEffect(() => { root.current?.querySelector<HTMLElement>("h3")?.focus(); }, []);
  function move(id: string, target: number) {
    if (disabled || pending) return;
    setIds(current => { const next = current.filter(value => value !== id); next.splice(target, 0, id); return next; });
    window.requestAnimationFrame(() => root.current?.querySelector<HTMLElement>(`[data-media-id="${id}"] strong`)?.focus());
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); if (disabled || pending) return;
    if (editor.kind === "order") await onOrder(ids);
    else if (selected && editor.kind === "description" && text.trim().length <= 200) await onDescription(selected.id, text);
    else if (selected && editor.kind === "remove") await onRemove(selected.id);
  }
  return <form ref={root} className="media-editor" aria-labelledby="media-editor-title" noValidate onSubmit={submit}
    onKeyDown={event => { if (event.key === "Escape" && !pending) { event.preventDefault(); onCancel(); } }}>
    <h3 id="media-editor-title" tabIndex={-1}>{t(editor.kind === "order" ? "mediaOrder" : editor.kind === "description" ? "mediaAltTitle" : "confirmRemoveTitle", { name: selected?.altText || t("imageNumber", { number: editor.images.findIndex(image => image.id === selected?.id) + 1 }) })}</h3>
    {editor.kind === "order" ? <><p>{t("mediaOrderHint")}</p>
      <ol className="media-order-list">{ids.map((id, index) => {
        const image = editor.images.find(image => image.id === id)!;
        return <li key={id} data-media-id={id}><img className="media-order-thumbnail" src={image.url} alt="" loading="lazy" />
          <strong tabIndex={-1}>{index + 1}. {image.altText || t("imageNumber", { number: editor.images.indexOf(image) + 1 })}</strong>
          {index === 0 && <span>{t("mediaFirst")}</span>}<div className="cluster">
            <Button type="button" variant="secondary" disabled={disabled || pending || index === 0} onClick={() => move(id, index - 1)}>{t("mediaEarlier")}</Button>
            <Button type="button" variant="secondary" disabled={disabled || pending || index === ids.length - 1} onClick={() => move(id, index + 1)}>{t("mediaLater")}</Button>
            <Button type="button" variant="secondary" disabled={disabled || pending || index === 0} onClick={() => move(id, 0)}>{t("mediaMakeFirst")}</Button>
          </div></li>;
      })}</ol></> : editor.kind === "description" ? <Field id="media-altText" name="altText" label={t("altText")} hint={t("mediaAltHint")} maxLength={200}
      value={text} disabled={disabled || pending} error={text.trim().length > 200 ? t("validation.altTextInvalid") : undefined} onChange={event => setText(event.target.value)} /> : <p>{t("confirmRemoveBody")}</p>}
    <div className="cluster"><Button type="submit" variant={editor.kind === "remove" ? "danger" : "primary"} disabled={disabled || text.trim().length > 200 && editor.kind === "description"} busy={pending} busyLabel={t("mediaWorking")}>
      {t(editor.kind === "remove" ? "confirmRemove" : "mediaSave")}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button></div>
  </form>;
}
