import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { NavLink } from "react-router";
import type { Category } from "../api";
import { categoryPath } from "../publicPages";

type CategoryState =
  | { status: "loading" }
  | { status: "ready"; categories: Category[] }
  | { status: "error"; retry: () => void };

export function CategoryNavigation({
  state,
  className = "",
  onNavigate,
  contentLanguage,
}: {
  state: CategoryState;
  className?: string;
  onNavigate?: () => void;
  contentLanguage: string;
}) {
  const { t } = useTranslation("navigation");
  return (
    <nav className={className} aria-label={t("categories")}>
      <NavLink to="/" end onClick={onNavigate}>
        {t("allProducts")}
      </NavLink>
      {state.status === "loading" && (
        <span className="navigation-status" role="status">
          {t("categoriesLoading")}
        </span>
      )}
      {state.status === "error" && (
        <span className="navigation-status" role="alert">
          {t("categoriesFailed")}{" "}
          <button
            type="button"
            className="link-button compact-link"
            onClick={state.retry}
          >
            {t("retryCategories")}
          </button>
        </span>
      )}
      {state.status === "ready" && state.categories.length === 0 && (
        <span className="navigation-status">{t("categoriesEmpty")}</span>
      )}
      {state.status === "ready" &&
        state.categories.map((category) => (
          <NavLink
            key={category.slug}
            to={categoryPath(category.slug)}
            lang={contentLanguage}
            onClick={onNavigate}
          >
            {category.name}
          </NavLink>
        ))}
    </nav>
  );
}

export function MobileNavigation({
  state,
  contentLanguage,
}: {
  state: CategoryState;
  contentLanguage: string;
}) {
  const { t } = useTranslation("navigation");
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    panel.current?.querySelector<HTMLElement>("a, button")?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setOpen(false);
      trigger.current?.focus();
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open]);

  return (
    <div className="mobile-navigation">
      <button
        ref={trigger}
        type="button"
        className="menu-trigger"
        aria-expanded={open}
        aria-controls="store-category-menu"
        onClick={() => setOpen((current) => !current)}
      >
        {open ? t("closeMenu") : t("menu")}
      </button>
      {open && (
        <div
          ref={panel}
          id="store-category-menu"
          className="mobile-navigation-panel"
        >
          <CategoryNavigation
            state={state}
            className="mobile-category-links"
            onNavigate={() => setOpen(false)}
            contentLanguage={contentLanguage}
          />
        </div>
      )}
    </div>
  );
}

export type { CategoryState };
