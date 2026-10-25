import { useLayoutEffect } from "react";
import { useLocation, useNavigate } from "react-router";
import { installPageMetadata } from "./pageMetadata";
import { categoryPath, productPath } from "./publicPages";

export function usePageMetadata(data: unknown, kind: "catalog" | "product" | "content") {
  const location = useLocation();
  useLayoutEffect(() => installPageMetadata(data, kind), [data, kind, location.key]);
}

// A followed API redirect can return the current slug; it is not a page-level HTTP redirect.
export function useCurrentSlug(kind: "catalog" | "product", requested: string | undefined, returned: string | undefined, pathCategory = false) {
  const location = useLocation(), navigate = useNavigate();
  const moved = !!requested && typeof returned === "string" && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(returned) && returned !== requested;
  useLayoutEffect(() => {
    if (!moved) return;
    const query = new URLSearchParams(location.search);
    if (kind === "catalog" && !pathCategory) query.set("category", returned);
    void navigate({ pathname: kind === "product" ? productPath(returned) : pathCategory ? categoryPath(returned) : location.pathname,
      search: query.toString() ? "?" + query : "", hash: location.hash }, { replace: true });
  }, [kind, moved, returned, pathCategory, location.pathname, location.search, location.hash, navigate]);
  return moved;
}
