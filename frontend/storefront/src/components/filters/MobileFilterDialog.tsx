import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { Facet } from "../../api";
import { FilterPanel } from "./FilterPanel";

type Props = {
  facets: Facet[];
  activeCount: number;
  onChange: (code: string, value: string | null) => void;
  onClear: () => void;
};

export function MobileFilterDialog({
  facets,
  activeCount,
  onChange,
  onClear,
}: Props) {
  const { t } = useTranslation("catalog");
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const titleId = useId();

  function close() {
    setOpen(false);
    window.setTimeout(() => trigger.current?.focus(), 0);
  }

  useEffect(() => {
    if (!open) return;
    const container = panel.current;
    container?.querySelector<HTMLElement>("button, input, select")?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        close();
        return;
      }
      if (event.key !== "Tab" || !container) return;
      const controls = [...container.querySelectorAll<HTMLElement>(
        'button:not(:disabled), input:not(:disabled), select:not(:disabled), [href], [tabindex]:not([tabindex="-1"])',
      )].filter((element) => element.getClientRects().length > 0);
      const first = controls[0];
      const last = controls.at(-1);
      if (!first || !last) return;
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open]);

  if (facets.length === 0) return null;

  return (
    <div className="mobile-filter-controls">
      <button
        ref={trigger}
        type="button"
        className="mobile-filter-trigger"
        aria-expanded={open}
        aria-controls="catalog-filter-dialog"
        onClick={() => setOpen(true)}
      >
        {activeCount > 0
          ? t("filterButtonActive", { count: activeCount })
          : t("filterButton")}
      </button>
      {open && (
        <div
          className="filter-dialog-backdrop"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) close();
          }}
        >
          <div
            ref={panel}
            id="catalog-filter-dialog"
            className="filter-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby={titleId}
          >
            <div className="filter-dialog-header">
              <h2 id={titleId}>{t("filterProducts")}</h2>
              <button type="button" onClick={close}>
                {t("closeFilters")}
              </button>
            </div>
            <FilterPanel
              facets={facets}
              onChange={onChange}
              onClear={onClear}
              showHeading={false}
            />
          </div>
        </div>
      )}
    </div>
  );
}
