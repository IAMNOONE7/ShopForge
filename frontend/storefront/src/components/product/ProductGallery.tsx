import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { useTranslation } from "react-i18next";
import type { ProductDetail } from "../../api";

type ProductImage = ProductDetail["images"][number];

export function ProductGallery({ images, productName, contentLanguage }: {
  images: ProductImage[];
  productName: string;
  contentLanguage?: string;
}) {
  const { t } = useTranslation("catalog");
  const [selectedUrl, setSelectedUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState<Set<string>>(() => new Set());
  const [expanded, setExpanded] = useState(false);
  const gallery = useRef<HTMLElement>(null);
  const expand = useRef<HTMLButtonElement>(null);
  const image = images.find((candidate) => candidate.url === selectedUrl) ?? images[0];
  const selected = images.findIndex((candidate) => candidate === image);

  function markFailed(url: string) {
    setFailed((current) => new Set(current).add(url));
  }

  function browse(event: KeyboardEvent<HTMLDivElement>) {
    const controls = [...event.currentTarget.querySelectorAll<HTMLButtonElement>("button")];
    const current = controls.indexOf(event.target as HTMLButtonElement);
    if (current < 0) return;
    let next: number;
    switch (event.key) {
      case "ArrowRight": next = (current + 1) % images.length; break;
      case "ArrowLeft": next = (current - 1 + images.length) % images.length; break;
      case "Home": next = 0; break;
      case "End": next = images.length - 1; break;
      default: return;
    }
    event.preventDefault();
    setSelectedUrl(images[next].url);
    controls[next].focus();
  }

  return (
    <section ref={gallery} className="product-gallery" aria-label={t("productGallery")} tabIndex={-1}>
      <div className="product-main-image">
        <GalleryPhoto image={image} failed={image ? failed.has(image.url) : false}
          productName={productName} contentLanguage={contentLanguage} onError={markFailed} priority />
        {image && !failed.has(image.url) && (
          <button ref={expand} type="button" className="product-expand-image" onClick={() => setExpanded(true)}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
              <path d="M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5" />
            </svg>
            {t("viewLargerImage")}
          </button>
        )}
      </div>
      {images.length > 1 && (
        <>
          <div className="product-thumbnails" role="toolbar" aria-label={t("productImages")} onKeyDown={browse}>
            {images.map((candidate, index) => (
              <button key={candidate.url} type="button" className="product-thumbnail-button"
                aria-label={t("showImage", { current: index + 1, total: images.length })}
                aria-pressed={index === selected} tabIndex={index === selected ? 0 : -1}
                onClick={() => setSelectedUrl(candidate.url)}>
                {failed.has(candidate.url) ? (
                  <span aria-hidden="true" className="product-thumbnail-placeholder" />
                ) : (
                  <img src={candidate.url} alt="" width={96} height={96} loading="lazy"
                    onError={() => markFailed(candidate.url)} />
                )}
              </button>
            ))}
          </div>
          <p className="product-image-count" aria-live="polite" aria-atomic="true">
            {t("imagePosition", { current: selected + 1, total: images.length })}
          </p>
        </>
      )}
      {expanded && image && (
        <ExpandedImage image={image} failed={failed.has(image.url)} productName={productName}
          contentLanguage={contentLanguage} onError={markFailed} onClose={() => {
            setExpanded(false);
            (expand.current ?? gallery.current)?.focus();
          }} />
      )}
    </section>
  );
}

function GalleryPhoto({ image, failed, productName, contentLanguage, onError, priority = false }: {
  image: ProductImage | undefined;
  failed: boolean;
  productName: string;
  contentLanguage?: string;
  onError: (url: string) => void;
  priority?: boolean;
}) {
  const { t } = useTranslation("catalog");
  if (!image || failed) {
    return (
      <div className="product-image product-image-placeholder" role="img" aria-label={t("imageUnavailable")}>
        <svg viewBox="0 0 32 32" fill="none" stroke="currentColor" strokeWidth="1" aria-hidden="true">
          <rect x="4.5" y="4.5" width="23" height="23" rx="2" />
          <path d="M5 24l8-9 6 6 4-5 4 4M3 3l26 26" />
        </svg>
        <span aria-hidden="true">{t("imageUnavailable")}</span>
      </div>
    );
  }
  return (
    <img src={image.url} alt={image.altText?.trim() || productName} lang={contentLanguage}
      className="product-image" width={960} height={960} loading="eager"
      fetchPriority={priority ? "high" : "auto"} onError={() => onError(image.url)} />
  );
}

function ExpandedImage({ image, failed, productName, contentLanguage, onError, onClose }: {
  image: ProductImage;
  failed: boolean;
  productName: string;
  contentLanguage?: string;
  onError: (url: string) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation(["catalog", "common"]);
  const dialog = useRef<HTMLDialogElement>(null);
  const close = useRef<HTMLButtonElement>(null);
  const mounted = useRef(false);
  const title = useId();
  useEffect(() => {
    mounted.current = true;
    const viewer = dialog.current;
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    viewer?.showModal();
    close.current?.focus();
    return () => {
      mounted.current = false;
      document.body.style.overflow = overflow;
      if (viewer?.open) viewer.close();
    };
  }, []);

  return (
    <dialog ref={dialog} className="product-image-viewer" aria-modal="true" aria-labelledby={title}
      onClose={() => {
        // A cleanup close may arrive after Strict Mode has opened the viewer again.
        if (mounted.current && !dialog.current?.open) onClose();
      }}
      onCancel={(event) => { event.preventDefault(); dialog.current?.close(); }}
      onKeyDown={(event) => {
        if (event.key === "Tab") { event.preventDefault(); close.current?.focus(); }
      }}
      onClick={(event) => {
        if (event.target !== event.currentTarget) return;
        const bounds = event.currentTarget.getBoundingClientRect();
        if (event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.current?.close();
      }}>
      <div className="product-image-viewer-header">
        <h2 id={title}>{t("catalog:productGallery")} · <bdi lang={contentLanguage}>{productName}</bdi></h2>
        <button ref={close} type="button" onClick={() => dialog.current?.close()}>
          {t("common:close")} <span aria-hidden="true">×</span>
        </button>
      </div>
      <div className="product-image-viewer-media">
        <GalleryPhoto image={image} failed={failed} productName={productName}
          contentLanguage={contentLanguage} onError={onError} />
      </div>
    </dialog>
  );
}
