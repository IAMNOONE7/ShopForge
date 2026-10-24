import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { isAbortError } from "./api/http";
import { statusOf } from "./api/errors";
import type { RequestState } from "./useRequest";

export type SetupRefreshMode = "active" | "paused" | "hidden" | "limit" | "error" | "idle" | "pending";
const loading = { status: "loading" } as const;
export function useSetupProgress<T>(key: string, load: (signal: AbortSignal) => Promise<T>, active: (data: T) => boolean) {
  const [result, setResult] = useState<{ key: string; state: RequestState<T> } | null>(null);
  const saved = useRef(result), current = useRef({ key, load }), mounted = useRef(false);
  useLayoutEffect(() => { current.current = { key, load }; }, [key, load]);
  const controller = useRef<AbortController | null>(null), sequence = useRef(0), blocked = useRef(false);
  const [suspendedKey, setSuspendedKey] = useState<string | null>(null);
  const suspended = suspendedKey === key;
  const [visible, setVisible] = useState(() => document.visibilityState === "visible");
  const [paused, setPaused] = useState(false), [reads, setReads] = useState(0);
  const publish = useCallback((scope: string, state: RequestState<T>) => {
    saved.current = { key: scope, state }; setResult(saved.current);
  }, []);
  const stopRead = useCallback(() => {
    sequence.current++; controller.current?.abort(); controller.current = null;
  }, []);
  const reload = useCallback(async () => {
    if (!mounted.current || blocked.current || current.current.key !== key) return;
    stopRead(); const attempt = sequence.current, request = new AbortController(); controller.current = request;
    const previous = saved.current?.key === key && saved.current.state.status === "ready" ? saved.current.state : null;
    publish(key, previous ? { ...previous, refreshing: true, refreshError: null } : { status: "loading" });
    const owns = () => mounted.current && current.current.key === key && attempt === sequence.current && !request.signal.aborted;
    try {
      const data = await current.current.load(request.signal);
      if (owns()) publish(key, { status: "ready", data, refreshing: false, refreshError: null });
    } catch (error) {
      if (!owns() || isAbortError(error)) return;
      publish(key, previous ? { ...previous, refreshing: false, refreshError: error } : statusOf(error) === 404 ? { status: "not-found", error } : { status: "error", error });
    } finally { if (attempt === sequence.current) controller.current = null; }
  }, [key, publish, stopRead]);
  useEffect(() => {
    mounted.current = true; blocked.current = false; void reload();
    return () => { mounted.current = false; stopRead(); };
  }, [reload, stopRead]);
  useEffect(() => {
    const changed = () => setVisible(document.visibilityState === "visible");
    document.addEventListener("visibilitychange", changed);
    return () => document.removeEventListener("visibilitychange", changed);
  }, []);
  const state: RequestState<T> = result?.key === key ? result.state : loading;
  const failed = state.status === "error" || state.status === "not-found" || state.status === "ready" && state.refreshError !== null;
  const running = state.status === "ready" && active(state.data);
  const mode: SetupRefreshMode = suspended ? "pending" : failed ? "error" : !running ? "idle" : paused ? "paused" : !visible ? "hidden" : reads >= 12 ? "limit" : "active";
  useEffect(() => {
    if (mode !== "active" || state.status !== "ready" || state.refreshing) return;
    const timer = window.setTimeout(() => { if (!blocked.current) { setReads(count => count + 1); void reload(); } }, 5_000);
    return () => window.clearTimeout(timer);
  }, [mode, state, reload]);
  return { state, mode, suspended, canPause: running && !failed && !suspended, paused, refreshing: state.status === "loading" || state.status === "ready" && state.refreshing,
    refresh: () => { setReads(0); void reload(); }, toggle: () => { setPaused(value => !value); setReads(0); },
    isCurrent: (data: T) => mounted.current && current.current.key === key && !blocked.current && controller.current === null
      && saved.current?.key === key && saved.current.state.status === "ready" && !saved.current.state.refreshing && saved.current.state.refreshError === null && saved.current.state.data === data,
    suspend: () => { if (mounted.current && current.current.key === key) { blocked.current = true; stopRead(); setSuspendedKey(key); } },
    // An accepted write must invalidate any older read before its authoritative detail is displayed.
    accept: (data: T) => { if (mounted.current && current.current.key === key) { stopRead(); publish(key, { status: "ready", data, refreshing: false, refreshError: null }); } },
    resume: () => { if (mounted.current && current.current.key === key) { blocked.current = false; setSuspendedKey(null); setReads(0); void reload(); } } };
}
