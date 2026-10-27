import type { FeedCategory, StoreFeed } from "../api/feeds";

export const googleFeed: StoreFeed = { feed: "google", isEnabled: true,
  url: "https://workshop.example.test/api/storefront/feeds/google.xml?token=fixture-google-access",
  lastRunAt: "2026-10-26T10:00:00Z", lastMilliseconds: 58, lastBytes: 3096, lastProducts: 12, lastSkipped: 0, lastError: null };
export const heurekaFeed: StoreFeed = { feed: "heureka", isEnabled: false, url: null,
  lastRunAt: null, lastMilliseconds: null, lastBytes: null, lastProducts: null, lastSkipped: null, lastError: null };
export const zboziFeed: StoreFeed = { ...googleFeed, feed: "zbozi", isEnabled: false,
  url: "https://workshop.example.test/api/storefront/feeds/zbozi.xml?token=fixture-zbozi-access", lastError: "The stored file remains <unchanged>." };
export const feedCategories: FeedCategory[] = [
  { categoryId: "aaaaaaaa-1111-2222-3333-444444444444", name: "Chairs", engineCategory: "Home > Furniture > Chairs" },
  { categoryId: "bbbbbbbb-1111-2222-3333-444444444444", name: "Tables", engineCategory: null },
];
