import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import {
  bundledResources,
  fallbackLanguage,
  supportedLanguages,
} from "./catalog";
import {
  readStoredLanguage,
  resolveLanguage,
  writeStoredLanguage,
} from "./language";

const storageKey = "shopforge.admin.uiLanguage";
let memoryLanguage: string | null = null;

function browserLanguages() {
  return typeof navigator === "undefined" ? [] : navigator.languages;
}
function browserStorage() {
  if (typeof window === "undefined") return undefined;
  try {
    return window.localStorage;
  } catch {
    return undefined;
  }
}
function applyLanguage(language: string) {
  memoryLanguage = language;
  if (typeof document !== "undefined") document.documentElement.lang = language;
  writeStoredLanguage(browserStorage(), storageKey, language);
}

export async function initializeI18n() {
  if (i18n.isInitialized) return i18n;
  const stored =
    memoryLanguage ?? readStoredLanguage(browserStorage(), storageKey);
  const language = resolveLanguage(
    stored,
    browserLanguages(),
    supportedLanguages,
    fallbackLanguage,
  );
  await i18n.use(initReactI18next).init({
    resources: bundledResources,
    lng: language,
    fallbackLng: fallbackLanguage,
    supportedLngs: supportedLanguages,
    defaultNS: "common",
    interpolation: { escapeValue: false },
    returnNull: false,
    saveMissing: import.meta.env.DEV,
    missingKeyHandler: (_languages, namespace, key) =>
      console.warn(`Missing translation: ${namespace}:${key}`),
  });
  applyLanguage(language);
  i18n.on("languageChanged", applyLanguage);
  return i18n;
}

export { i18n };
