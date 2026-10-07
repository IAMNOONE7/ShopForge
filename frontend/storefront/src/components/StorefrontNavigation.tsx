import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { NavLink } from "react-router";
import type { Category } from "../api";
import { categoryPath } from "../publicPages";
import { categoryTree, type CategoryNode } from "./categoryTree";

type CategoryState =
  | { status: "loading" }
  | { status: "ready"; categories: Category[] }
  | { status: "error"; retry: () => void };

export function CategoryNavigation({
  state,
  className = "",
  onNavigate,
  contentLanguage,
  variant = "list",
}: {
  state: CategoryState;
  className?: string;
  onNavigate?: () => void;
  contentLanguage: string;
  variant?: "desktop" | "list";
}) {
  const { t } = useTranslation("navigation");
  const roots = state.status === "ready" ? categoryTree(state.categories) : [];
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
      {state.status === "ready" && (variant === "desktop" ? (
        <ul className="desktop-category-list">
          {roots.slice(0, 5).map((node) => (
            <li key={node.category.slug}>
              {node.children.length > 0 ? (
                <CategoryMenu node={node} contentLanguage={contentLanguage} />
              ) : (
                <NavLink to={categoryPath(node.category.slug)} lang={contentLanguage} onClick={onNavigate}>
                  {node.category.name}
                </NavLink>
              )}
            </li>
          ))}
          {roots.length > 5 && (
            <li><CategoryMenu overflow={roots.slice(5)} contentLanguage={contentLanguage} /></li>
          )}
        </ul>
      ) : (
        <CategoryLinks nodes={roots} contentLanguage={contentLanguage} onNavigate={onNavigate} />
      ))}
    </nav>
  );
}

function CategoryLinks({ nodes, contentLanguage, onNavigate }: {
  nodes: CategoryNode[];
  contentLanguage: string;
  onNavigate?: () => void;
}) {
  return (
    <ul className="category-tree-links">
      {nodes.map(({ category, children }) => (
        <li key={category.slug}>
          <NavLink to={categoryPath(category.slug)} lang={contentLanguage} onClick={onNavigate}>
            {category.name}
          </NavLink>
          {children.length > 0 && (
            <CategoryLinks nodes={children} contentLanguage={contentLanguage} onNavigate={onNavigate} />
          )}
        </li>
      ))}
    </ul>
  );
}

function CategoryMenu({ node, overflow = [], contentLanguage }: {
  node?: CategoryNode;
  overflow?: CategoryNode[];
  contentLanguage: string;
}) {
  const { t } = useTranslation("navigation");
  const menu = useRef<HTMLDetailsElement>(null);
  const summary = useRef<HTMLElement>(null);
  const panel = useRef<HTMLDivElement>(null);

  function alignPanel() {
    if (!menu.current?.open || !summary.current || !panel.current) return;
    const space = document.documentElement.clientWidth - summary.current.getBoundingClientRect().left;
    panel.current.classList.toggle("align-end", space < panel.current.offsetWidth + 16);
  }

  useEffect(() => {
    function dismiss(event: PointerEvent) {
      const details = menu.current;
      if (details && event.target instanceof Node && !details.contains(event.target)) {
        details.open = false;
      }
    }
    document.addEventListener("pointerdown", dismiss);
    window.addEventListener("resize", alignPanel);
    return () => {
      document.removeEventListener("pointerdown", dismiss);
      window.removeEventListener("resize", alignPanel);
    };
  }, []);

  function close() {
    if (menu.current) menu.current.open = false;
  }

  return (
    <details
      ref={menu}
      className="desktop-category-menu"
      onToggle={alignPanel}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) close();
      }}
      onKeyDown={(event) => {
        if (event.key === "Escape" && menu.current?.open) {
          event.preventDefault();
          close();
          summary.current?.focus();
        }
      }}
    >
      <summary ref={summary} lang={node ? contentLanguage : undefined}>
        {node ? node.category.name : t("moreCategories")}<span aria-hidden="true">⌄</span>
      </summary>
      <div ref={panel} className="desktop-category-panel">
        {node && <NavLink to={categoryPath(node.category.slug)} className="category-menu-all" onClick={close}>
          {t("allInCategory", { name: node.category.name })}
        </NavLink>}
        <CategoryLinks nodes={node?.children ?? overflow} contentLanguage={contentLanguage} onNavigate={close} />
      </div>
    </details>
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
        aria-label={open ? t("closeMenu") : t("menu")}
        aria-controls="store-category-menu"
        onClick={() => setOpen((current) => !current)}
      >
        <span aria-hidden="true">{open ? "×" : "+"}</span>
        {t("menu")}
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
