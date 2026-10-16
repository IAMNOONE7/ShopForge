import { useEffect, useRef, useState } from "react";
import { api, type StoreProduct } from "./api";
import { maxBulkListings, type BulkListingChange, type BulkListingResult } from "./api/bulkListings";
import { isHttpError } from "./api/http";
import { useAction } from "./useAction";
import type { BulkListingAction } from "./utils/bulkListings";

export type SelectedListing = Pick<StoreProduct, "id" | "name" | "sku">;
export type BulkListingIntent = { action: BulkListingAction; change: BulkListingChange; categoryNames: { add: string[]; remove: string[] } };
type Confirmation = BulkListingIntent & { listings: SelectedListing[] };
type Outcome = { intent: Confirmation } & ({ kind: "success"; result: BulkListingResult } | { kind: "refused" | "uncertain"; error: unknown });

export function useBulkListings(storeId: string, archived: boolean, ready: boolean, reload: () => void) {
  const [selection, setSelection] = useState<Map<string, SelectedListing>>(new Map());
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [revision, setRevision] = useState(0);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(true);
  const latestReload = useRef(reload);
  useEffect(() => { latestReload.current = reload; }, [reload]);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const locked = pending || confirmation !== null || outcome?.kind === "uncertain" || !ready;

  function toggle(listings: SelectedListing[], checked: boolean) {
    if (locked) return;
    setOutcome(null);
    setSelection(current => {
      const next = new Map(current);
      for (const listing of listings) { if (checked) next.set(listing.id, { id: listing.id, name: listing.name, sku: listing.sku }); else next.delete(listing.id); }
      return next.size <= maxBulkListings ? next : current;
    });
  }
  function clear() { if (!locked) { setSelection(new Map()); setOutcome(null); } }
  function prepare(intent: BulkListingIntent) {
    if (locked || !selection.size || selection.size > maxBulkListings || (archived ? intent.action !== "restore" : intent.action === "restore")) return;
    setOutcome(null); setConfirmation({ ...intent, listings: [...selection.values()] });
  }
  function cancel() { if (!pending) setConfirmation(null); }
  async function confirm() {
    if (!confirmation || !ready || pending || outcome?.kind === "uncertain") return;
    const intent = confirmation;
    await run(async () => {
      setOutcome(null);
      try {
        const result = await api.changeBulkListings(storeId, intent.listings.map(listing => listing.id), intent.change);
        if (!active.current) return;
        setOutcome({ kind: "success", intent, result }); setSelection(new Map()); setConfirmation(null);
      } catch (error) {
        if (!active.current) return;
        const refused = isHttpError(error) && error.kind === "http" && error.status !== null && [400, 401, 403, 404, 405, 415, 422, 429].includes(error.status);
        setOutcome({ kind: refused ? "refused" : "uncertain", intent, error }); setConfirmation(null);
      } finally {
        if (active.current) { setRevision(current => current + 1); latestReload.current(); }
      }
    });
  }
  function finishReview() {
    if (pending || !ready || outcome?.kind !== "uncertain") return;
    setSelection(new Map()); setOutcome(null); setRevision(current => current + 1);
  }
  return { selection, confirmation, outcome, pending, locked, ready, revision, toggle, clear, prepare, cancel, confirm, finishReview };
}
