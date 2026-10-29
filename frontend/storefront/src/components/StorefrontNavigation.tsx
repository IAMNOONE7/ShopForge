import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { NavLink, useLocation } from "react-router";
import type { Category } from "../api";
import { categoryPath } from "../publicPages";
import { categoryTree, type CategoryNode } from "./categoryTree";
import { StoreInformationLinks } from "./StoreInformationLinks";

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
  const allProducts = <NavLink to="/" end onClick={onNavigate}>{t("allProducts")}</NavLink>;
  return (
    <nav className={className} aria-label={t("categories")}>
      {variant !== "desktop" && allProducts}
      {variant === "desktop" && (
        <ul className="desktop-category-list">
          <li>{allProducts}</li>
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
          {roots.length > 5 && <li><CategoryMenu overflow={roots.slice(5)} contentLanguage={contentLanguage} /></li>}
        </ul>
      )}
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
      {state.status === "ready" && variant !== "desktop" && (
        <CategoryLinks nodes={roots} contentLanguage={contentLanguage} onNavigate={onNavigate} />
      )}
    </nav>
  );
}

function CategoryLinks({ nodes, contentLanguage, onNavigate, depth = 0, label }: {
  nodes: CategoryNode[];
  contentLanguage: string;
  onNavigate?: () => void;
  depth?: number;
  label?: string;
}) {
  return (
    <ul className="category-tree-links" aria-label={label}>
      {nodes.map(node => <CategoryBranch key={node.category.slug} node={node} depth={depth}
        contentLanguage={contentLanguage} onNavigate={onNavigate} />)}
    </ul>
  );
}

function containsPath(nodes: CategoryNode[], pathname: string): boolean {
  return nodes.some(node => categoryPath(node.category.slug) === pathname || containsPath(node.children, pathname));
}

function CategoryBranch({ node: { category, children }, depth, contentLanguage, onNavigate }: {
  node: CategoryNode;
  depth: number;
  contentLanguage: string;
  onNavigate?: () => void;
}) {
  const { t } = useTranslation("navigation");
  const { pathname } = useLocation();
  const activeChild = containsPath(children, pathname);
  const [expanded, setExpanded] = useState(depth === 0 || activeChild);
  const controls = useId();
  useEffect(() => { if (activeChild) setExpanded(true); }, [activeChild, pathname]);
  return <li className={`category-branch${children.length ? " category-branch-parent" : ""}`}>
    <div className="category-branch-row">
      <NavLink className="category-branch-link" to={categoryPath(category.slug)} lang={contentLanguage} onClick={onNavigate}>
        {category.name}
      </NavLink>
      {children.length > 0 && <button type="button" className="category-branch-toggle"
        aria-label={t(expanded ? "hideSubcategories" : "showSubcategories", { name: category.name })}
        aria-expanded={expanded} aria-controls={controls} onClick={() => setExpanded(current => !current)}>
        <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="m6 9 6 6 6-6" /></svg>
      </button>}
    </div>
    {children.length > 0 && <div className="category-branch-children" id={controls} hidden={!expanded}>
      <CategoryLinks nodes={children} depth={depth + 1} contentLanguage={contentLanguage} onNavigate={onNavigate}
        label={t("subcategoriesOf", { name: category.name })} />
    </div>}
  </li>;
}

function CategoryMenu({ node, overflow = [], contentLanguage }: {
  node?: CategoryNode;
  overflow?: CategoryNode[];
  contentLanguage: string;
}) {
  const { t } = useTranslation("navigation");
  const menu = useRef<HTMLDetailsElement>(null);
  const summary = useRef<HTMLElement>(null);

  function opened() {
    if (!menu.current?.open) return;
    menu.current.closest("nav")?.querySelectorAll<HTMLDetailsElement>(".desktop-category-menu[open]").forEach(other => {
      if (other !== menu.current) other.open = false;
    });
  }

  useEffect(() => {
    function dismiss(event: PointerEvent) {
      const details = menu.current;
      if (details && event.target instanceof Node && !details.contains(event.target)) {
        details.open = false;
      }
    }
    document.addEventListener("pointerdown", dismiss);
    return () => {
      document.removeEventListener("pointerdown", dismiss);
    };
  }, []);

  function close() {
    if (menu.current) menu.current.open = false;
  }

  return (
    <details
      ref={menu}
      className="desktop-category-menu"
      onToggle={opened}
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
      <div className="desktop-category-panel">
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
          <StoreInformationLinks onNavigate={() => setOpen(false)} />
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
