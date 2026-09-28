export function baseLanguage(value: string) {
  return value.trim().toLowerCase().split("-")[0];
}

export function resolveLanguage(
  stored: string | null,
  browserLanguages: readonly string[],
  supported: readonly string[],
  fallback: string,
) {
  const candidates = stored ? [stored, ...browserLanguages] : browserLanguages;
  for (const candidate of candidates) {
    const language = baseLanguage(candidate);
    if (supported.includes(language)) return language;
  }
  return fallback;
}

export function readStoredLanguage(
  storage: Pick<Storage, "getItem"> | undefined,
  key: string,
) {
  try {
    return storage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

export function writeStoredLanguage(
  storage: Pick<Storage, "setItem"> | undefined,
  key: string,
  language: string,
) {
  try {
    storage?.setItem(key, language);
  } catch {
    return;
  }
}
