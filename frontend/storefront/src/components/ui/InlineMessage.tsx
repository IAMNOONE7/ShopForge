import type { ReactNode } from "react";
import styles from "./ui.module.css";

export function InlineMessage({
  children,
  title,
  tone = "info",
}: {
  children: ReactNode;
  title?: ReactNode;
  tone?: "info" | "success" | "error";
}) {
  return (
    <div
      className={[styles.inlineMessage, styles[tone]].join(" ")}
      role={tone === "error" ? "alert" : "status"}
    >
      {title && <strong>{title}</strong>}
      <div>{children}</div>
    </div>
  );
}
