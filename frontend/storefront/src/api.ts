import { requestJson } from "./api/http";

export type Category = {
  name: string;
  slug: string;
};

export type ProductSummary = {
  id: string;
  slug: string;
  name: string;
  price: number;
  available: number;
  rating: number;
  reviewCount: number;
  imageUrl: string | null;
};

export type AttributeType =
  | "text"
  | "integer"
  | "decimal"
  | "boolean"
  | "date"
  | "select"
  | "multiSelect";

export type Facet = {
  code: string;
  name: string;
  type: AttributeType;
  unit: string | null;
  options:
    | { code: string; name: string; count: number; selected: boolean }[]
    | null;
  min: number | string | null;
  max: number | string | null;
  selectedMin: number | string | null;
  selectedMax: number | string | null;
  trueCount: number | null;
  falseCount: number | null;
  selected: boolean | null;
};

export type ProductPage = {
  items: ProductSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
  filters: Facet[];
};

export type ProductAttribute = {
  code: string;
  name: string;
  type: AttributeType;
  unit: string | null;
  value: string | number | boolean | string[];
};

export type ProductVariant = {
  id: string;
  optionValues: string[];
  available: number;
};

export type ProductDetail = {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  price: number;
  available: number;
  optionNames: string[];
  variants: ProductVariant[];
  rating: number;
  reviewCount: number;
  images: { url: string; altText: string | null }[];
  categories: Category[];
  attributes: ProductAttribute[];
};

export function getCategories(signal: AbortSignal) {
  return requestJson<Category[]>("/api/storefront/categories", { signal });
}

// `query` carries filters (f.<code>), sort and page exactly as they appear in the page URL.
export function getProducts(
  category: string | undefined,
  query: URLSearchParams,
  signal: AbortSignal,
) {
  const parameters = new URLSearchParams(query);
  if (category) {
    parameters.set("category", category);
  }
  return requestJson<ProductPage>(`/api/storefront/products?${parameters}`, {
    signal,
  });
}

export type Review = {
  author: string;
  rating: number;
  text: string;
  writtenAt: string;
};

export type Reviews = {
  canWrite: boolean;
  reviews: Review[];
};

export function getReviews(slug: string, signal: AbortSignal) {
  return requestJson<Reviews>(
    `/api/storefront/products/${encodeURIComponent(slug)}/reviews`,
    { signal },
  );
}

export function getProduct(slug: string, signal: AbortSignal) {
  return requestJson<ProductDetail>(
    `/api/storefront/products/${encodeURIComponent(slug)}`,
    { signal },
  );
}
