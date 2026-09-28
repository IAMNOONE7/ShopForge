export function CatalogLoading({ label }: { label: string }) {
  return (
    <div className="catalog-loading" role="status" aria-live="polite">
      <span className="sr-only">{label}</span>
      <div className="catalog-loading-grid" aria-hidden="true">
        {Array.from({ length: 6 }, (_, index) => (
          <div className="catalog-card-skeleton" key={index}>
            <span className="catalog-image-skeleton" />
            <span />
            <span />
          </div>
        ))}
      </div>
    </div>
  );
}
