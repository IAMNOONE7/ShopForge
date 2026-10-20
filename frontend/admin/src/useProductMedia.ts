import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { api, type ProductImage } from "./api";
import { HttpError, isHttpError } from "./api/http";
import { checkedProductImages } from "./api/productMedia";

export function uncertainMediaWrite(error: unknown) {
  return !isHttpError(error) || error.kind !== "http" || error.status === null || error.status >= 500 || error.status === 408;
}

type Inventory = { images: ProductImage[]; error: unknown; needsRead: boolean };

export function useProductMedia({ productId, images, canManage, refreshing, refreshFailed }: {
  productId: string; images: ProductImage[]; canManage: boolean; refreshing: boolean; refreshFailed: boolean;
}) {
  const incoming = JSON.stringify(images);
  const initial = useMemo<Inventory>(() => {
    try { return { images: checkedProductImages(productId, images), error: null, needsRead: false }; }
    catch (error) { return { images: [], error, needsRead: true }; }
  }, [productId, images]);
  const [saved, setSaved] = useState<(Inventory & { source: string }) | null>(null);
  const current = saved?.source === incoming ? saved : initial;
  const [busy, setBusy] = useState(false);
  const active = useRef(false), lock = useRef(false), mustRead = useRef(current.needsRead);
  const controller = useRef<AbortController | null>(null);
  const inventory = useRef(current.images);
  const latest = useRef({ canManage, refreshing, refreshFailed, incoming });
  useLayoutEffect(() => {
    latest.current = { canManage, refreshing, refreshFailed, incoming };
    inventory.current = current.images; mustRead.current = current.needsRead;
  }, [canManage, refreshing, refreshFailed, incoming, current]);
  useEffect(() => {
    active.current = true;
    return () => { active.current = false; controller.current?.abort(); };
  }, []);

  function writable() {
    const scope = latest.current;
    return active.current && !lock.current && !mustRead.current && scope.canManage && !scope.refreshing && !scope.refreshFailed;
  }
  async function read() {
    controller.current?.abort(); const next = new AbortController(); controller.current = next;
    const source = latest.current.incoming;
    mustRead.current = true;
    setSaved({ source, images: inventory.current, error: null, needsRead: true });
    try {
      const result = checkedProductImages(productId, await api.productImages(productId, next.signal));
      if (!active.current || next.signal.aborted || latest.current.incoming !== source) return false;
      inventory.current = result; mustRead.current = false;
      setSaved({ source, images: result, error: null, needsRead: false });
      return true;
    } catch (error) {
      if (active.current && !next.signal.aborted && latest.current.incoming === source)
        setSaved({ source, images: inventory.current, error, needsRead: true });
      return false;
    }
  }
  async function refresh() {
    if (!active.current || lock.current) return;
    lock.current = true; setBusy(true);
    try { await read(); } finally { lock.current = false; if (active.current) setBusy(false); }
  }
  async function change(write: () => Promise<unknown>) {
    if (!writable()) return { blocked: true, acknowledged: false, fresh: false, error: null };
    lock.current = true; setBusy(true);
    let acknowledged = false, error: unknown = null;
    try { await write(); acknowledged = true; } catch (failure) { error = failure; }
    if (!active.current) return { blocked: false, acknowledged, fresh: false, error };
    // Even an error can follow a committed write. Read before allowing another action.
    const fresh = await read();
    lock.current = false; if (active.current) setBusy(false);
    return { blocked: false, acknowledged, fresh, error };
  }
  function checkNewImage(value: ProductImage) {
    if (inventory.current.some(image => image.id.toLowerCase() === value.id.toLowerCase())) throw new HttpError("invalid-response", 200);
  }
  return { images: current.images, readError: current.error, needsRead: current.needsRead, busy,
    canWrite: !busy && !current.needsRead && canManage && !refreshing && !refreshFailed,
    refresh, change, checkNewImage };
}
