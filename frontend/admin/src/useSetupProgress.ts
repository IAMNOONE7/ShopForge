import { useEffect, useState } from "react";
import { useRequest } from "./useRequest";

export type SetupRefreshMode = "active" | "paused" | "hidden" | "limit" | "error" | "idle";
export function useSetupProgress<T>(key: string, load: (signal: AbortSignal) => Promise<T>, active: (data: T) => boolean) {
  const [state, reload] = useRequest(key, load);
  const [visible, setVisible] = useState(() => document.visibilityState === "visible");
  const [paused, setPaused] = useState(false), [reads, setReads] = useState(0);
  useEffect(() => {
    const changed = () => setVisible(document.visibilityState === "visible");
    document.addEventListener("visibilitychange", changed);
    return () => document.removeEventListener("visibilitychange", changed);
  }, []);
  const failed = state.status === "error" || state.status === "not-found" || state.status === "ready" && state.refreshError !== null;
  const running = state.status === "ready" && active(state.data);
  const mode: SetupRefreshMode = failed ? "error" : !running ? "idle" : paused ? "paused" : !visible ? "hidden" : reads >= 12 ? "limit" : "active";
  useEffect(() => {
    if (mode !== "active" || state.status !== "ready" || state.refreshing) return;
    const timer = window.setTimeout(() => { setReads(count => count + 1); reload(); }, 5_000);
    return () => window.clearTimeout(timer);
  }, [mode, state, reload]);
  return { state, mode, canPause: running && !failed, paused, refreshing: state.status === "loading" || state.status === "ready" && state.refreshing,
    refresh: () => { setReads(0); reload(); }, toggle: () => { setPaused(value => !value); setReads(0); } };
}
