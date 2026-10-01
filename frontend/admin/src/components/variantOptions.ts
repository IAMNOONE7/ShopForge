import type { Product, ProductVariant } from "../api";

export function variantOptions(product: Product, variant: ProductVariant) {
  return product.optionNames
    .map((name, index) => name + ": " + variant.optionValues[index])
    .join(" · ");
}
