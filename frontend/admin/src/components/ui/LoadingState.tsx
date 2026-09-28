import styles from "./ui.module.css";

export function LoadingState({
  label,
  lines = 3,
}: {
  label: string;
  lines?: number;
}) {
  return (
    <div className={styles.loadingState} role="status" aria-live="polite">
      <span className="sr-only">{label}</span>
      <div aria-hidden="true" className={styles.skeletonGroup}>
        {Array.from({ length: lines }, (_, index) => (
          <span key={index} className={styles.skeleton} />
        ))}
      </div>
    </div>
  );
}
