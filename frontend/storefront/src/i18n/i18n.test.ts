import { createInstance } from "i18next";
import { describe, expect, it } from "vitest";
import {
  fallbackLanguage,
  localeCatalogs,
  supportedLanguages,
} from "./catalog";
import { readStoredLanguage, resolveLanguage } from "./language";

function flatten(value: object, prefix = ""): Map<string, string> {
  const entries = new Map<string, string>();
  for (const [key, child] of Object.entries(value)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof child === "string") entries.set(path, child);
    else entriesFor(entries, flatten(child as object, path));
  }
  return entries;
}

function entriesFor(target: Map<string, string>, source: Map<string, string>) {
  for (const entry of source) target.set(...entry);
}

function placeholders(value: string) {
  return [...value.matchAll(/{{\s*([^},\s]+)[^}]*}}/g)]
    .map((match) => match[1])
    .sort();
}

describe("storefront locale catalogs", () => {
  it("discovers English and Czech with matching keys and interpolation", () => {
    expect(supportedLanguages).toEqual(["cs", "en"]);
    const english = flatten(
      localeCatalogs.find((locale) => locale.code === "en")!.resources,
    );
    const czech = flatten(
      localeCatalogs.find((locale) => locale.code === "cs")!.resources,
    );
    expect([...czech.keys()]).toEqual([...english.keys()]);
    for (const [key, value] of english)
      expect(placeholders(czech.get(key)!)).toEqual(placeholders(value));
  });

  it("uses Czech and English plural rules for representative counts", async () => {
    const instance = createInstance();
    await instance.init({
      resources: Object.fromEntries(
        localeCatalogs.map((locale) => [locale.code, locale.resources]),
      ),
      fallbackLng: fallbackLanguage,
      defaultNS: "common",
    });
    const counts = [0, 1, 2, 4, 5, 21];
    await instance.changeLanguage("en");
    expect(
      counts.map((count) => instance.t("catalog:productCount", { count })),
    ).toEqual([
      "0 products",
      "1 product",
      "2 products",
      "4 products",
      "5 products",
      "21 products",
    ]);
    await instance.changeLanguage("cs");
    expect(
      counts.map((count) => instance.t("catalog:productCount", { count })),
    ).toEqual([
      "0 produktů",
      "1 produkt",
      "2 produkty",
      "4 produkty",
      "5 produktů",
      "21 produktů",
    ]);
  });

  it("resolves saved and browser languages safely", () => {
    expect(
      resolveLanguage("cs-CZ", ["en-US"], supportedLanguages, fallbackLanguage),
    ).toBe("cs");
    expect(
      resolveLanguage("de-DE", ["cs-CZ"], supportedLanguages, fallbackLanguage),
    ).toBe("cs");
    expect(
      resolveLanguage(null, ["de-DE"], supportedLanguages, fallbackLanguage),
    ).toBe("en");
    const blocked = {
      getItem: () => {
        throw new Error("blocked");
      },
    };
    expect(readStoredLanguage(blocked, "language")).toBeNull();
  });
});
