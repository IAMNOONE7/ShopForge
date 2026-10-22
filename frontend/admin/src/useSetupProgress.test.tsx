// @vitest-environment jsdom
import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { useSetupProgress } from "./useSetupProgress";
import { HttpError } from "./api/http";

beforeEach(() => { vi.useFakeTimers(); vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible"); });
afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); });
const settle = async () => { await act(async () => {}); };
const tick = async (ms = 5_000) => { await act(async () => { await vi.advanceTimersByTimeAsync(ms); }); };

it("bounds automatic reads, avoids overlap and lets manual refresh start a new budget", async () => {
  const load = vi.fn<(signal: AbortSignal) => Promise<boolean>>().mockResolvedValue(true), { result } = renderHook(() => useSetupProgress("scope", load, value => value)); await settle();
  for (let n = 0; n < 12; n++) await tick();
  expect(load).toHaveBeenCalledTimes(13); expect(result.current.mode).toBe("limit");
  await tick(60_000); expect(load).toHaveBeenCalledTimes(13);
  act(() => result.current.refresh()); await settle(); expect(load).toHaveBeenCalledTimes(14);
  await tick(); expect(load).toHaveBeenCalledTimes(15);
});
it("stops in hidden tabs, respects an explicit pause and removes its timer on unmount", async () => {
  const load = vi.fn<(signal: AbortSignal) => Promise<boolean>>().mockResolvedValue(true), hook = renderHook(() => useSetupProgress("scope", load, value => value)); await settle();
  vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden"); act(() => document.dispatchEvent(new Event("visibilitychange")));
  await tick(60_000); expect(load).toHaveBeenCalledOnce(); expect(hook.result.current.mode).toBe("hidden");
  vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible"); act(() => document.dispatchEvent(new Event("visibilitychange")));
  act(() => hook.result.current.toggle()); await tick(); expect(load).toHaveBeenCalledOnce(); expect(hook.result.current.mode).toBe("paused");
  act(() => hook.result.current.refresh()); await settle(); expect(load).toHaveBeenCalledTimes(2); expect(hook.result.current.mode).toBe("paused");
  act(() => hook.result.current.toggle()); await tick(); expect(load).toHaveBeenCalledTimes(3);
  hook.unmount(); await tick(60_000); expect(load).toHaveBeenCalledTimes(3);
});
it("stops on terminal/human-wait states and read failures without automatically retrying", async () => {
  const load = vi.fn<(signal: AbortSignal) => Promise<boolean>>().mockResolvedValue(true), { result } = renderHook(() => useSetupProgress("scope", load, value => value)); await settle();
  load.mockResolvedValueOnce(false); await tick(); expect(result.current.mode).toBe("idle"); await tick(60_000); expect(load).toHaveBeenCalledTimes(2);
  load.mockRejectedValueOnce(new HttpError("http", 503)); act(() => result.current.refresh()); await settle(); expect(result.current.mode).toBe("error");
  await tick(60_000); expect(load).toHaveBeenCalledTimes(3);
});
it("does not overlap a slow read and aborts the in-flight request on departure", async () => {
  let signal: AbortSignal | undefined;
  const load = vi.fn<(signal: AbortSignal) => Promise<boolean>>().mockResolvedValueOnce(true).mockImplementation((value: AbortSignal) => { signal = value; return new Promise<boolean>(() => {}); });
  const hook = renderHook(() => useSetupProgress("scope", load, value => value)); await settle(); await tick(); await tick(60_000);
  expect(load).toHaveBeenCalledTimes(2); hook.unmount(); expect(signal?.aborted).toBe(true);
});
