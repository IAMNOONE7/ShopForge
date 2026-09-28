import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { ProductDetail } from "../../api";

type ProductImage = ProductDetail["images"][number];

export function ProductGallery({
  images,
  productName,
}: {
  images: ProductImage[];
  productName: string;
}) {
  const { t } = useTranslation("catalog");
  const [selected, setSelected] = useState(0);
  const [failed, setFailed] = useState<Set<string>>(() => new Set());
  const image = images[selected] ?? images[0];

  function markFailed(url: string) {
    setFailed((current) => new Set(current).add(url));
  }

  if (!image) {
    return (
      <div className="product-gallery">
        <ImagePlaceholder label={t("imageUnavailable")} />
      </div>
    );
  }

  return (
    <div className="product-gallery" aria-label={t("productGallery")}>
      <div className="product-main-image">
        {failed.has(image.url) ? (
          <ImagePlaceholder label={t("imageUnavailable")} />
        ) : (
          <img
            src={image.url}
            alt={image.altText ?? productName}
            className="product-image"
            onError={() => markFailed(image.url)}
          />
        )}
      </div>
      {images.length > 1 && (
        <div className="product-thumbnails" aria-label={t("productImages")}>
          {images.map((candidate, index) => (
            <button
              key={`${candidate.url}:${index}`}
              type="button"
              className="product-thumbnail-button"
              aria-label={t("showImage", {
                current: index + 1,
                total: images.length,
              })}
              aria-pressed={index === selected}
              onClick={() => setSelected(index)}
            >
              {failed.has(candidate.url) ? (
                <span aria-hidden="true" className="product-thumbnail-placeholder" />
              ) : (
                <img
                  src={candidate.url}
                  alt=""
                  width="96"
                  height="72"
                  onError={() => markFailed(candidate.url)}
                />
              )}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function ImagePlaceholder({ label }: { label: string }) {
  return (
    <div className="product-image product-image-placeholder" role="img" aria-label={label}>
      <span>{label}</span>
    </div>
  );
}
