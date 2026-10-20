import { useCallback, useEffect, useRef, useState } from "react";
import { HttpError, isHttpError } from "./api/http";
import { useAction } from "./useAction";
import { useRequest } from "./useRequest";

export type MethodRecord = { code: string; name: string; providerKey: string; isActive: boolean };
type Outcome = { kind: "acknowledged" | "refused" | "uncertain"; error: unknown | null };
export function useMethodEditor<T extends MethodRecord>({ storeId, kind, load, loadProviders, allowed, storeStale, refreshToken, onAttempt }: {
  storeId: string; kind: "payment" | "shipping"; load: (storeId: string, signal?: AbortSignal) => Promise<T[]>;
  loadProviders: (storeId: string, signal?: AbortSignal) => Promise<string[]>; allowed: boolean; storeStale: boolean;
  refreshToken: number; onAttempt: () => void;
}) {
  const [state, reload] = useRequest(`${kind}-methods:${storeId}`, async signal => ({ items: await load(storeId, signal) }));
  const [providers, reloadProviders] = useRequest(`${kind}-providers:${storeId}`, signal => loadProviders(storeId, signal));
  const [editor, setEditor] = useState<{ item: T | null } | null>(null), [outcome, setOutcome] = useState<Outcome | null>(null);
  const [invalidated, setInvalidated] = useState<object | null>(null);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(true), opener = useRef<HTMLButtonElement | null>(null), heading = useRef<HTMLHeadingElement>(null), result = useRef<HTMLDivElement>(null);
  const headingRef = useCallback((node: HTMLHeadingElement | null) => { heading.current = node; }, []);
  const resultRef = useCallback((node: HTMLDivElement | null) => { result.current = node; }, []);
  const attempt = useRef(onAttempt); useEffect(() => { attempt.current = onAttempt; }, [onAttempt]);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => { if (refreshToken > 0) { reload(); reloadProviders(); } }, [refreshToken, reload, reloadProviders]);
  useEffect(() => { if (outcome) result.current?.focus(); }, [outcome]);
  const ready = state.status === "ready" && !state.refreshing && state.refreshError === null && state.data !== invalidated;
  const providerReady = providers.status === "ready" && !providers.refreshing && providers.refreshError === null;
  const items = state.status === "ready" ? state.data.items : [];
  const keys = providers.status === "ready" ? providers.data : [];
  const stale = editor?.item != null && !items.some(item => JSON.stringify(item) === JSON.stringify(editor.item));
  const disabled = !allowed || storeStale || !ready || pending || outcome?.kind === "uncertain";
  const editorDisabled = disabled || stale || editor?.item === null && !providerReady;
  function open(item: T | null, button: HTMLButtonElement) {
    if (disabled || editor || !item && !providerReady) return;
    opener.current = button; setOutcome(null); setEditor({ item });
  }
  function close() {
    if (pending) return; setEditor(null); setOutcome(null);
    window.requestAnimationFrame(() => { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); });
  }
  async function save(change: () => Promise<unknown>) {
    if (editorDisabled || !editor || state.status !== "ready") return;
    await run(async () => {
      setOutcome(null); setInvalidated(state.data);
      try {
        const saved = await change();
        if (editor.item && (!saved || typeof saved !== "object" || !("providerKey" in saved) || saved.providerKey !== editor.item.providerKey)) {
          throw new HttpError("invalid-response", null);
        }
        if (active.current) { setEditor(null); setOutcome({ kind: "acknowledged", error: null }); }
      } catch (error) {
        if (!active.current) return;
        const refused = isHttpError(error) && error.kind === "http" && error.status !== null && [400, 401, 403, 404, 409, 415, 422, 429].includes(error.status);
        if (!refused) setEditor(null);
        setOutcome({ kind: refused ? "refused" : "uncertain", error });
      } finally { if (active.current) { reload(); reloadProviders(); attempt.current(); } }
    });
  }
  function finishReview() { if (ready && !pending) { setOutcome(null); heading.current?.focus(); } }
  return { state, providers, keys, items, ready, providerReady, editor, outcome, pending, disabled, editorDisabled, stale,
    headingRef, resultRef, reload, reloadProviders, open, close, save, finishReview };
}
