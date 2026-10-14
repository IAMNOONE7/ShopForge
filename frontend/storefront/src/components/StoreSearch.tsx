import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate } from "react-router";
import { searchMaximum, searchMinimum } from "../search";

export function StoreSearch() {
  const location = useLocation();
  const catalog = location.pathname === "/" || location.pathname.startsWith("/c/");
  const query = catalog ? new URLSearchParams(location.search).get("q") ?? "" : "";
  return <SearchForm key={location.key} query={query} />;
}

function SearchForm({ query }: { query: string }) {
  const { t } = useTranslation("catalog");
  const navigate = useNavigate();
  const location = useLocation();
  const input = useRef<HTMLInputElement>(null);
  const [draft, setDraft] = useState(query);
  const [invalid, setInvalid] = useState(false);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const term = draft.trim();
    if (term && (term.length < searchMinimum || term.length > searchMaximum)) {
      setInvalid(true);
      input.current?.focus();
      return;
    }
    setInvalid(false);
    setDraft(term);
    const target = term ? `/?${new URLSearchParams({ q: term })}` : "/";
    if (target !== location.pathname + location.search) navigate(target);
  }

  function clear() {
    setDraft("");
    setInvalid(false);
    input.current?.focus();
    if (location.pathname !== "/" || location.search) navigate("/");
  }

  return (
    <form role="search" aria-label={t("searchLabel")} action="/" method="get" className="store-search" onSubmit={submit} noValidate>
      <label htmlFor="store-search-query" className="sr-only">{t("searchLabel")}</label>
      <div className="store-search-controls">
        <input
          ref={input}
          id="store-search-query"
          name="q"
          type="search"
          placeholder={t("searchPlaceholder")}
          minLength={searchMinimum}
          maxLength={searchMaximum}
          aria-describedby={invalid ? "store-search-error" : "store-search-hint"}
          aria-invalid={invalid || undefined}
          value={draft}
          onChange={(event) => { setDraft(event.target.value); setInvalid(false); }}
        />
        {draft && <button type="button" className="store-search-clear" aria-label={t("clearSearch")} onClick={clear}>
          <span aria-hidden="true">×</span>
        </button>}
        <button type="submit" className="store-search-submit" aria-label={t("searchAction")}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" aria-hidden="true">
            <circle cx="10.5" cy="10.5" r="6.5" />
            <path d="m15.5 15.5 5 5" />
          </svg>
        </button>
      </div>
      <span id="store-search-hint" className="sr-only">{t("searchLength")}</span>
      {invalid && <p id="store-search-error" className="store-search-error" role="alert">{t("searchLength")}</p>}
    </form>
  );
}
