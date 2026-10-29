export function ProductDetailLoading({ label }: { label: string }) {
  return (
    <div className="product-detail-loading" role="status" aria-live="polite">
      <span className="sr-only">{label}</span>
      <div aria-hidden="true">
        <div className="product-breadcrumbs product-breadcrumb-skeleton" />
        <div className="product-detail product-detail-skeleton">
          <div className="product-gallery-skeleton" />
          <div className="product-summary product-info-skeleton"><span /><span /><span /></div>
          <div className="product-purchase product-purchase-skeleton"><span /><span /><span /></div>
        </div>
      </div>
    </div>
  );
}
