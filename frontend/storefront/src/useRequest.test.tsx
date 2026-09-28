// @vitest-environment jsdom
import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { HttpError } from "./api/http";
import { useRequest } from "./useRequest";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}

describe("useRequest", () => {
  it("aborts and ignores a stale result when the request key changes", async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const load = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockImplementationOnce((signal) => {
        expect(signal.aborted).toBe(false);
        return first.promise;
      })
      .mockImplementationOnce(() => second.promise);

    const { result, rerender } = renderHook(
      ({ key }) => useRequest(key, load),
      {
        initialProps: { key: "store-a" },
      },
    );
    rerender({ key: "store-b" });
    expect(result.current.status).toBe("loading");

    await act(async () => {
      first.resolve("old store");
      second.resolve("new store");
      await Promise.all([first.promise, second.promise]);
    });
    await waitFor(() => expect(result.current.status).toBe("ready"));
    expect(result.current.status === "ready" && result.current.data).toBe(
      "new store",
    );
  });

  it("retains labeled data when a same-key refresh fails", async () => {
    const refresh = deferred<string>();
    const load = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockResolvedValueOnce("current")
      .mockImplementationOnce(() => refresh.promise);
    const { result } = renderHook(() => useRequest("same", load));
    await waitFor(() => expect(result.current.status).toBe("ready"));

    act(() => result.current.reload());
    await waitFor(() =>
      expect(
        result.current.status === "ready" && result.current.refreshing,
      ).toBe(true),
    );
    await act(async () => {
      refresh.reject(new HttpError("network", null));
      await refresh.promise.catch(() => undefined);
    });

    await waitFor(() =>
      expect(
        result.current.status === "ready" && result.current.refreshError,
      ).toBeInstanceOf(HttpError),
    );
    expect(result.current.status === "ready" && result.current.data).toBe(
      "current",
    );
  });

  it("exposes previous ready data only for an allowed key transition", async () => {
    const next = deferred<string>();
    const load = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockResolvedValueOnce("first page")
      .mockImplementationOnce(() => next.promise);
    const { result, rerender } = renderHook(
      ({ key }) =>
        useRequest(key, load, {
          retainPrevious: (previousKey) =>
            previousKey.startsWith("catalog:") && key.startsWith("catalog:"),
        }),
      { initialProps: { key: "catalog:page-1" } },
    );
    await waitFor(() => expect(result.current.status).toBe("ready"));

    rerender({ key: "catalog:page-2" });
    expect(result.current.status).toBe("loading");
    expect(result.current.transitionData).toBe("first page");

    await act(async () => {
      next.resolve("second page");
      await next.promise;
    });
    await waitFor(() => expect(result.current.status).toBe("ready"));
    expect(result.current.transitionData).toBeUndefined();
  });

});
