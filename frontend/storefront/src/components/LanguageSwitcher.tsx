import { useTranslation } from "react-i18next";
import { fallbackLanguage, localeCatalogs } from "../i18n/catalog";

export function LanguageSwitcher() {
  const { t, i18n } = useTranslation("common");
  const selected = i18n.resolvedLanguage?.split("-")[0] ?? fallbackLanguage;

  return (
    <label className="language-switcher">
      <span>{t("language")}</span>
      <select
        value={selected}
        onChange={(event) => void i18n.changeLanguage(event.target.value)}
      >
        {localeCatalogs.map((locale) => (
          <option key={locale.code} value={locale.code} lang={locale.code}>
            {locale.nativeName}
          </option>
        ))}
      </select>
    </label>
  );
}
