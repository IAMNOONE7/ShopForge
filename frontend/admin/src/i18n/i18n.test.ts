import { createInstance } from "i18next";
import { describe, expect, it } from "vitest";
import {
  fallbackLanguage,
  localeCatalogs,
  supportedLanguages,
} from "./catalog";
import { readStoredLanguage, resolveLanguage } from "./language";
import { currencyFormatter, uiLocale } from "../utils/format";

function flatten(value: object, prefix = ""): Map<string, string> {
  const result = new Map<string, string>();
  for (const [key, child] of Object.entries(value)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof child === "string") result.set(path, child);
    else
      for (const entry of flatten(child as object, path)) result.set(...entry);
  }
  return result;
}
function placeholders(value: string) {
  return [...value.matchAll(/{{\s*([^},\s]+)[^}]*}}/g)]
    .map((match) => match[1])
    .sort();
}

describe("admin locale catalogs", () => {
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

  it("uses Czech plural rules for representative counts", async () => {
    const instance = createInstance();
    await instance.init({
      resources: Object.fromEntries(
        localeCatalogs.map((locale) => [locale.code, locale.resources]),
      ),
      fallbackLng: fallbackLanguage,
      defaultNS: "common",
    });
    await instance.changeLanguage("cs");
    expect(
      [0, 1, 2, 4, 5, 21].map((count) =>
        instance.t("reviews:waiting", { count }),
      ),
    ).toEqual([
      "0 čeká na vyřízení.",
      "1 čeká na vyřízení.",
      "2 čekají na vyřízení.",
      "4 čekají na vyřízení.",
      "5 čeká na vyřízení.",
      "21 čeká na vyřízení.",
    ]);
  });

  it("resolves language safely and formats admin values with the UI locale", () => {
    expect(
      resolveLanguage("cs-CZ", ["en-US"], supportedLanguages, fallbackLanguage),
    ).toBe("cs");
    expect(
      resolveLanguage("de-DE", ["cs-CZ"], supportedLanguages, fallbackLanguage),
    ).toBe("cs");
    expect(
      readStoredLanguage(
        {
          getItem: () => {
            throw new Error("blocked");
          },
        },
        "language",
      ),
    ).toBeNull();
    expect(uiLocale("cs")).toBe("cs-CZ");
    expect(currencyFormatter("EUR", "en").resolvedOptions().locale).toMatch(
      /^en/,
    );
  });
});
