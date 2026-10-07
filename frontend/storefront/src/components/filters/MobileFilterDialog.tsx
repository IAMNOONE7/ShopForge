import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { Facet } from "../../api";
import { FilterPanel } from "./FilterPanel";

type Props = {
  facets: Facet[];
  activeCount: number;
  resultCount: number;
  refreshing: boolean;
  contentLanguage?: string;
  onChange: (code: string, value: string | null) => void;
  onClear: () => void;
};

export function MobileFilterDialog({
  facets, activeCount, resultCount, refreshing, contentLanguage, onChange, onClear,
}: Props) {
  const { t } = useTranslation("catalog");
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const dialogId = useId();

  useEffect(() => {
    if (!open) return;
    const sheet = dialog.current;
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    sheet?.showModal();
    sheet?.querySelector<HTMLButtonElement>("button")?.focus();
    return () => {
      document.body.style.overflow = overflow;
      if (sheet?.open) sheet.close();
    };
  }, [open]);

  if (facets.length === 0 && !open) return null;

  return (
    <div className="mobile-filter-controls">
      <button
        ref={trigger}
        type="button"
        className="mobile-filter-trigger"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={dialogId}
        onClick={() => setOpen(true)}
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
          <path d="M3 6h18M3 12h18M3 18h18M8 3v6M16 9v6M10 15v6" />
        </svg>
        {activeCount > 0 ? t("filterButtonActive", { count: activeCount }) : t("filterButton")}
      </button>
      {open && (
        <dialog
          ref={dialog}
          id={dialogId}
          className="filter-dialog"
          aria-modal="true"
          aria-labelledby={titleId}
          onKeyDown={(event) => {
            if (event.key !== "Tab") return;
            const controls = event.currentTarget.querySelectorAll<HTMLElement>(
              'button:not(:disabled), input:not(:disabled):not([type="radio"]), input[type="radio"]:checked:not(:disabled), select:not(:disabled), [href], [tabindex]:not([tabindex="-1"])',
            );
            const first = controls[0];
            const last = controls[controls.length - 1];
            if (event.shiftKey && document.activeElement === first) {
              event.preventDefault();
              last?.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
              event.preventDefault();
              first?.focus();
            }
          }}
          onClose={() => {
            setOpen(false);
            if (facets.length > 0) trigger.current?.focus();
            else document.getElementById("shop-products")?.focus();
          }}
          onCancel={(event) => {
            event.preventDefault();
            dialog.current?.close();
          }}
          onClick={(event) => {
            if (event.target !== event.currentTarget) return;
            const bounds = event.currentTarget.getBoundingClientRect();
            if (event.clientX < bounds.left || event.clientX > bounds.right ||
                event.clientY < bounds.top || event.clientY > bounds.bottom) {
              dialog.current?.close();
            }
          }}
        >
          <div className="filter-dialog-header">
            <h2 id={titleId}>{t("filterProducts")}</h2>
            <button type="button" className="filter-dialog-close" onClick={() => dialog.current?.close()}>
              {t("closeFilters")} <span aria-hidden="true">×</span>
            </button>
          </div>
          <div className="filter-dialog-body">
            <p className="filter-dialog-hint">{t("filterHint")}</p>
            <FilterPanel
              facets={facets}
              onChange={onChange}
              onClear={onClear}
              showHeading={false}
              contentLanguage={contentLanguage}
            />
          </div>
          <div className="filter-dialog-footer">
            <button
              type="button"
              className="filter-show-results"
              disabled={refreshing}
              aria-live="polite"
              onClick={() => dialog.current?.close()}
            >
              {refreshing ? t("updatingResults") : t("showProducts", { count: resultCount })}
            </button>
          </div>
        </dialog>
      )}
    </div>
  );
}
