import { useId, type InputHTMLAttributes, type ReactNode } from "react";
import styles from "./ui.module.css";

type FieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, "id"> & {
  id?: string;
  label: ReactNode;
  hint?: ReactNode;
  error?: ReactNode;
};

export function Field({
  id,
  label,
  hint,
  error,
  className,
  ...inputProps
}: FieldProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const hintId = hint ? `${inputId}-hint` : undefined;
  const errorId = error ? `${inputId}-error` : undefined;
  const describedBy =
    [inputProps["aria-describedby"], hintId, errorId]
      .filter(Boolean)
      .join(" ") || undefined;

  return (
    <div className={styles.field}>
      <label htmlFor={inputId}>{label}</label>
      <input
        {...inputProps}
        id={inputId}
        className={[styles.input, className].filter(Boolean).join(" ")}
        aria-describedby={describedBy}
        aria-invalid={error ? true : inputProps["aria-invalid"]}
      />
      {hint && (
        <span id={hintId} className={styles.fieldHint}>
          {hint}
        </span>
      )}
      {error != null && (
        <span id={errorId} className={styles.fieldError}>
          {error}
        </span>
      )}
    </div>
  );
}
