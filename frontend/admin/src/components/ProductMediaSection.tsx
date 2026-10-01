import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type ProductImage } from "../api";
import { useAction } from "../useAction";
import {
  validateProductImage,
  type ProductIssue,
} from "./productValidation";
import { ProductIssueSummary } from "./ProductIssueSummary";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";
import { InlineMessage } from "./ui/InlineMessage";
import { RequestError } from "./ui/RequestError";

export function ProductMediaSection({
  productId,
  sku,
  images,
  reloadProducts,
}: {
  productId: string;
  sku: string;
  images: ProductImage[];
  reloadProducts: () => void;
}) {
  const { t } = useTranslation("products");
  const [addedImages, setAddedImages] = useState<ProductImage[]>([]);
  const [removedIds, setRemovedIds] = useState<string[]>([]);
  const shownImages = [
    ...images.filter((image) => !removedIds.includes(image.id)),
    ...addedImages.filter((image) =>
      !images.some((existing) => existing.id === image.id) &&
      !removedIds.includes(image.id),
    ),
  ];
  const [file, setFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const previewUrlRef = useRef<string | null>(null);
  const [altText, setAltText] = useState("");
  const [issues, setIssues] = useState<ProductIssue[]>([]);
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const [saved, setSaved] = useState<"added" | "removed" | null>(null);
  const [uploadError, runUpload, uploading] = useAction(reloadProducts);
  const [removeError, runRemove, removing] = useAction(reloadProducts);
  const fileInput = useRef<HTMLInputElement>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);
  const removeButton = useRef<HTMLButtonElement>(null);
  const mediaHeading = useRef<HTMLHeadingElement>(null);

  useEffect(() => () => {
    if (previewUrlRef.current) URL.revokeObjectURL(previewUrlRef.current);
  }, []);

  useEffect(() => {
    if (confirmId) confirmButton.current?.focus();
  }, [confirmId]);

  function chooseFile(selected: File | null) {
    if (previewUrlRef.current) URL.revokeObjectURL(previewUrlRef.current);
    const url =
      selected &&
      selected.size <= 5 * 1024 * 1024 &&
      ["image/jpeg", "image/png", "image/webp"].includes(selected.type)
        ? URL.createObjectURL(selected)
        : null;
    previewUrlRef.current = url;
    setPreviewUrl(url);
    setFile(selected);
    setIssues([]);
    setSaved(null);
  }

  async function upload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextIssues = validateProductImage(file, altText);
    setIssues(nextIssues);
    if (nextIssues.length) {
      window.requestAnimationFrame(() =>
        document.getElementById("product-image-validation")?.focus(),
      );
      return;
    }
    if (!file) return;
    await runUpload(async () => {
      const image = await api.uploadProductImage(productId, file, altText.trim());
      setAddedImages((current) => [...current, image]);
      chooseFile(null);
      setAltText("");
      setSaved("added");
      if (fileInput.current) fileInput.current.value = "";
    });
  }

  function closeConfirmation(removed = false) {
    setConfirmId(null);
    window.requestAnimationFrame(() => {
      if (removed) mediaHeading.current?.focus();
      else removeButton.current?.focus();
    });
  }

  async function removeImage(image: ProductImage) {
    let succeeded = false;
    await runRemove(async () => {
      await api.deleteProductImage(productId, image.id);
      setRemovedIds((current) => [...current, image.id]);
      setSaved("removed");
      succeeded = true;
    });
    if (succeeded) closeConfirmation(true);
  }

  const selected = shownImages.find((image) => image.id === confirmId);

  return (
    <section className="physical-panel" aria-labelledby="physical-media-title">
      <h2 id="physical-media-title" ref={mediaHeading} tabIndex={-1}>
        {t("images")}
      </h2>
      <p className="hint">{t("mediaHint")}</p>
      {shownImages.length ? (
        <ul className="physical-image-grid">
          {shownImages.map((image, index) => (
            <li key={image.id}>
              <div className="physical-image-frame">
                <img
                  src={image.url}
                  alt={image.altText ?? t("imageFallbackAlt", { sku, number: index + 1 })}
                  loading="lazy"
                />
              </div>
              <p>{image.altText || t("noAltText")}</p>
              <Button
                type="button"
                variant="secondary"
                disabled={uploading || removing}
                onClick={(event) => {
                  removeButton.current = event.currentTarget;
                  setSaved(null);
                  setConfirmId(image.id);
                }}
              >
                {t("removeImageNamed", {
                  name: image.altText || t("imageNumber", { number: index + 1 }),
                })}
              </Button>
            </li>
          ))}
        </ul>
      ) : (
        <div className="physical-image-placeholder" role="status">
          {t("noImages")}
        </div>
      )}

      {selected && (
        <div className="product-remove-confirmation" role="group">
          <strong>
            {t("confirmRemoveTitle", {
              name: selected.altText || t("imageNumber", {
                number: shownImages.indexOf(selected) + 1,
              }),
            })}
          </strong>
          <p>{t("confirmRemoveBody")}</p>
          <div className="cluster">
            <Button
              ref={confirmButton}
              type="button"
              variant="danger"
              busy={removing}
              busyLabel={t("removingImage")}
              onClick={() => void removeImage(selected)}
            >
              {t("confirmRemove")}
            </Button>
            <Button
              type="button"
              variant="secondary"
              disabled={removing}
              onClick={() => closeConfirmation()}
            >
              {t("cancel")}
            </Button>
          </div>
        </div>
      )}

      <form className="product-media-form" noValidate onSubmit={upload}>
        <h3>{t("addImage")}</h3>
        <ProductIssueSummary id="product-image-validation" issues={issues} />
        <div className="product-media-inputs">
          <div className="product-file-field">
            <label htmlFor="file">{t("chooseImage")}</label>
            <input
              ref={fileInput}
              id="file"
              name="file"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              aria-invalid={issues.some((issue) => issue.field === "file") || undefined}
              aria-describedby="product-file-hint"
              onChange={(event) => chooseFile(event.target.files?.[0] ?? null)}
            />
            <span id="product-file-hint" className="hint">
              {file ? t("selectedFile", { name: file.name }) : t("imageRequirements")}
            </span>
          </div>
          <Field
            id="altText"
            name="altText"
            label={t("altText")}
            maxLength={300}
            hint={t("altTextHint")}
            value={altText}
            aria-invalid={issues.some((issue) => issue.field === "altText") || undefined}
            onChange={(event) => {
              setAltText(event.target.value);
              setIssues((current) => current.filter((issue) => issue.field !== "altText"));
            }}
          />
        </div>
        {previewUrl && (
          <div className="product-selected-preview">
            <img src={previewUrl} alt={t("selectedPreviewAlt")} />
          </div>
        )}
        <Button
          type="submit"
          busy={uploading}
          busyLabel={t("uploadingImage")}
          disabled={removing}
        >
          {uploadError !== null ? t("retryUpload") : t("uploadImage")}
        </Button>
        {uploadError !== null && (
          <RequestError error={uploadError} operation="write" />
        )}
      </form>
      {removeError !== null && (
        <RequestError error={removeError} operation="write" />
      )}
      {saved && (
        <InlineMessage tone="success">
          {t(saved === "added" ? "imageAdded" : "imageRemoved")}
        </InlineMessage>
      )}
    </section>
  );
}
