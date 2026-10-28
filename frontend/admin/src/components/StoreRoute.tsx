import type { ReactNode } from "react";
import { PermissionScope } from "./PermissionScope";

export function StoreRoute({
  title,
  allowed,
  children,
}: {
  title: string;
  allowed: boolean;
  children: ReactNode;
}) {
  return (
    <div className="admin-route-section">
      <h1>{title}</h1>
      <PermissionScope allowed={allowed}>{children}</PermissionScope>
    </div>
  );
}
