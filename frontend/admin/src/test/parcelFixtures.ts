import type { ShippingMethod } from "../api";
import type { CarrierService, Quote, QuoteInput, RateCard } from "../api/parcelShipping";
import { metadataListing } from "./metadataFixtures";
export const parcelVariantId = "dddddddd-1111-2222-3333-444444444444";
export const parcelOtherVariantId = "eeeeeeee-1111-2222-3333-444444444444";
export const parcelService: CarrierService = { key: "packeta-cz-zbox", name: "Z-BOX CZ", providerKey: "packeta", kind: "locker", country: "CZ", isActive: true, maxWeightGrams: 15000,
  largest: { lengthMm: 600, widthMm: 450, heightMm: 350 }, smallest: { lengthMm: 100, widthMm: 70, heightMm: 10 }, largestVolumeMm3: 94500000, maxSumOfSidesMm: null, maxParcels: 10, source: "Saved service limits", sourceDate: "2026-10-24" };
export const parcelMethod: ShippingMethod = { code: "zbox", name: "Z-BOX", providerKey: "packeta", isActive: false, requiresPickupPoint: true, price: 0, vatRate: 21, maxWeightGrams: null, countries: ["CZ"], taxClass: "standard",
  pricing: "perParcel", carrierService: parcelService.key, parcelMaxWeightGrams: null, parcelLengthMm: null, parcelWidthMm: null, parcelHeightMm: null,
  ratesFrom: "placeholder", ratesPricedOn: null, rateBands: [{ upToGrams: 2000, price: 79 }, { upToGrams: 5000, price: 99 }] };
export const parcelCard: RateCard = { source: "own", pricedOn: null, bands: [{ upToGrams: 2000, price: 0 }, { upToGrams: 5000, price: 90.5 }] };
export const parcelInput: QuoteInput = { country: "CZ", lines: [{ storeProductId: metadataListing.id, variantId: parcelVariantId, quantity: 2 }] };
export const parcelQuote: Quote = { isAvailable: true, currency: "CZK", total: 158, parcels: [
  { contents: [{ variantId: parcelVariantId, sku: "CHAIR-S", quantity: 1 }], grossWeightGrams: 1300, bandUpToGrams: 2000, price: 79, alone: false },
  { contents: [{ variantId: parcelVariantId, sku: "CHAIR-S", quantity: 1 }], grossWeightGrams: 1000, bandUpToGrams: 2000, price: 79, alone: true },
], unavailable: null };
export const parcelForms = [{ id: metadataListing.productId, variants: [{ id: parcelVariantId, sku: "CHAIR-S", optionValues: ["S"] }, { id: parcelOtherVariantId, sku: "CHAIR-L", optionValues: ["L"] }] }];
