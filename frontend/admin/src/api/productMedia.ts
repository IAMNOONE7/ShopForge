import type { ProductImage } from "../api";
import { HttpError, requestJson } from "./http";

const path = (productId: string) => `/api/admin/products/${encodeURIComponent(productId)}/images`;
const same = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

function imageOf(productId: string, value: unknown): ProductImage {
  if (typeof value !== "object" || value === null) throw new HttpError("invalid-response", 200);
  const image = value as ProductImage;
  if (typeof image.id !== "string" || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(image.id)
    || typeof image.url !== "string" || !same(image.url, `${path(productId)}/${image.id}`)
    || !(image.altText === null || typeof image.altText === "string")
    || !Number.isSafeInteger(image.position) || image.position < 0) throw new HttpError("invalid-response", 200);
  return image;
}

export function checkedProductImages(productId: string, value: unknown): ProductImage[] {
  if (!Array.isArray(value)) throw new HttpError("invalid-response", 200);
  const images = value.map(image => imageOf(productId, image)).sort((a, b) => a.position - b.position);
  if (new Set(images.map(image => image.id.toLowerCase())).size !== images.length
    || images.some((image, index) => image.position !== index)) throw new HttpError("invalid-response", 200);
  return images;
}

export async function getProductImages(productId: string, signal?: AbortSignal) {
  const products = await requestJson<unknown>("/api/admin/products", { signal });
  signal?.throwIfAborted();
  if (!Array.isArray(products)) throw new HttpError("invalid-response", 200);
  const matching = products.filter(value => typeof value?.id === "string" && same(value.id, productId));
  if (matching.length > 1) throw new HttpError("invalid-response", 200);
  const product = matching[0];
  if (!product) throw new HttpError("http", 404);
  return checkedProductImages(productId, product.images);
}

export async function uploadProductImage(productId: string, file: File, altText: string) {
  if (altText.trim().length > 200) throw new RangeError("Image description is too long.");
  if (file.size === 0 || file.size > 5 * 1024 * 1024 || !["image/jpeg", "image/png", "image/webp"].includes(file.type)) throw new RangeError("Invalid image file.");
  const body = new FormData(); body.set("file", file); body.set("altText", altText.trim());
  return imageOf(productId, await requestJson<unknown>(path(productId), { method: "POST", body }));
}

export async function reorderProductImages(productId: string, imageIds: string[]) {
  if (!imageIds.length || new Set(imageIds.map(id => id.toLowerCase())).size !== imageIds.length) throw new RangeError("Invalid image order.");
  const images = checkedProductImages(productId, await requestJson<unknown>(path(productId), { method: "PUT", body: { imageIds } }));
  if (images.length !== imageIds.length || images.some((image, index) => !same(image.id, imageIds[index]))) throw new HttpError("invalid-response", 200);
  return images;
}

export async function describeProductImage(productId: string, imageId: string, altText: string) {
  if (altText.trim().length > 200) throw new RangeError("Image description is too long.");
  const saved = imageOf(productId, await requestJson<unknown>(`${path(productId)}/${encodeURIComponent(imageId)}`, { method: "PUT", body: { altText: altText.trim() || null } }));
  if (!same(saved.id, imageId) || saved.altText !== (altText.trim() || null)) throw new HttpError("invalid-response", 200);
  return saved;
}

export async function deleteProductImage(productId: string, imageId: string) {
  const value = await requestJson<unknown>(`${path(productId)}/${encodeURIComponent(imageId)}`, { method: "DELETE" });
  if (value !== undefined) throw new HttpError("invalid-response", 200);
}
