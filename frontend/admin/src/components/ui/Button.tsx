import {
  forwardRef,
  type ButtonHTMLAttributes,
  type ReactNode,
} from "react";
import styles from "./ui.module.css";

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  busy?: boolean;
  busyLabel?: ReactNode;
  variant?: "primary" | "secondary" | "quiet" | "danger";
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  function Button(
    {
      busy = false,
      busyLabel,
      children,
      className,
      disabled,
      variant = "primary",
      ...props
    },
    ref,
  ) {
    const classes = [styles.button, styles[variant], className]
      .filter(Boolean)
      .join(" ");

    return (
      <button
        {...props}
        ref={ref}
        className={classes}
        disabled={disabled || busy}
        aria-busy={busy || undefined}
      >
        {busy ? (busyLabel ?? children) : children}
      </button>
    );
  },
);
