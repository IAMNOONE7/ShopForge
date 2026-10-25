import type { Category, StoreProduct } from "../api";
import type { ContentPage } from "../api/contentPages";
import type { MetadataKind, MetadataSnapshot, MetadataStore } from "../api/metadata";

export const metadataStore: MetadataStore = {
  id: "aaaaaaaa-1111-2222-3333-444444444444", name: "Dílna / Workshop", currency: "CZK", culture: "cs-CZ", status: "published",
  logoUrl: null, primaryHostName: "shop.localhost", returnWindowDays: 30,
  theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 },
  company: { legalName: "Dílna s.r.o.", line1: "Ulice 1", city: "Praha", postalCode: "11000", country: "CZ", registrationNumber: "12345678", vatNumber: "CZ12345678" },
  seo: { titleSuffix: "Dílna", description: "Store description", socialImageUrl: "https://images.example.test/store.png", noIndex: true },
};
export const metadataId = "bbbbbbbb-1111-2222-3333-444444444444", metadataOtherId = "cccccccc-1111-2222-3333-444444444444";
export const metadataListing: StoreProduct = {
  id: metadataId, productId: metadataOtherId, sku: "CHAIR-01", name: "Žlutá židle", slug: "zluta-zidle", description: "Saved listing text", price: 2499.5, vatRate: 21,
  isVisible: true, sortOrder: 3, categoryIds: [metadataOtherId, metadataStore.id].sort(), taxClass: "standard", archivedAt: null,
  seoTitle: "Exact title", seoDescription: "Saved description", seoSocialImageUrl: "https://images.example.test/item.png", seoNoIndex: true,
};
export const metadataCategory: Category = { id: metadataId, name: "Židle", slug: "zidle", sortOrder: 7, parentId: metadataOtherId, attributeIds: [metadataOtherId, metadataStore.id],
  seoTitle: "Exact title", seoDescription: "Saved description", pageText: "První odstavec.\n\n<script>literal</script>" };
export const metadataPage: ContentPage = { id: metadataId, title: "Podmínky", slug: "terms", body: "Original content\n\n<b>literal</b>", isPublished: true,
  seoTitle: "Exact title", seoDescription: "Saved description", seoNoIndex: true };
export function metadataFixture(kind: MetadataKind): MetadataSnapshot {
  const store = structuredClone(metadataStore);
  switch (kind) {
    case "store": return { kind, store };
    case "listing": return { kind, store, item: structuredClone(metadataListing) };
    case "category": return { kind, store, item: structuredClone(metadataCategory) };
    case "page": return { kind, store, item: structuredClone(metadataPage) };
  }
}
