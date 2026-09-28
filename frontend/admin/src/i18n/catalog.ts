export type LocaleModule = {
  code: string;
  nativeName: string;
  resources: Record<string, object>;
};

const modules = import.meta.glob<LocaleModule>("./locales/*.ts", {
  eager: true,
  import: "default",
});

export const localeCatalogs = Object.values(modules).sort((left, right) =>
  left.code.localeCompare(right.code),
);
export const supportedLanguages = localeCatalogs.map((locale) => locale.code);
export const fallbackLanguage = "en";

if (!localeCatalogs.some((locale) => locale.code === fallbackLanguage)) {
  throw new Error("The English fallback locale is missing.");
}

export const bundledResources = Object.fromEntries(
  localeCatalogs.map((locale) => [locale.code, locale.resources]),
);
