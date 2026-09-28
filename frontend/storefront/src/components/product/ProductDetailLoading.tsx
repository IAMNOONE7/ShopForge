export function ProductDetailLoading({ label }: { label: string }) {
  return (
    <div className="product-detail product-detail-skeleton">
      <span className="sr-only">{label}</span>
      <div className="product-gallery-skeleton" aria-hidden="true" />
      <div className="product-info-skeleton" aria-hidden="true">
        <span />
        <span />
        <span />
        <span />
      </div>
    </div>
  );
}
