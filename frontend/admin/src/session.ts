import { createContext, useContext } from "react";
import type { CurrentUser } from "./api";

export type AdminRole =
  | "Owner"
  | "Admin"
  | "CatalogManager"
  | "OrderManager"
  | "Warehouse"
  | "Support"
  | "unknown";

export type Session = {
  user: CurrentUser;
  logout: () => Promise<void>;
  logoutPending: boolean;
  logoutError: unknown | null;
};

export const SessionContext = createContext<Session | null>(null);

export function useSession(): Session {
  const session = useContext(SessionContext);

  if (!session) {
    throw new Error("useSession must be used inside SessionContext.");
  }

  return session;
}

export function knownRole(role: string): AdminRole {
  return [
    "Owner",
    "Admin",
    "CatalogManager",
    "OrderManager",
    "Warehouse",
    "Support",
  ].includes(role)
    ? (role as AdminRole)
    : "unknown";
}

export function canManageCatalog(role: string) {
  return ["Owner", "Admin", "CatalogManager"].includes(role);
}

export function canManageStore(role: string) {
  return ["Owner", "Admin"].includes(role);
}
