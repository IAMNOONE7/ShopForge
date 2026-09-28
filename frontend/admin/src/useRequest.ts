import { useCallback, useEffect, useRef, useState } from "react";
import { statusOf } from "./api/errors";
import { isAbortError } from "./api/http";

export type RequestState<T> =
  | { status: "loading" }
  | {
      status: "ready";
      data: T;
      refreshing: boolean;
      refreshError: unknown | null;
    }
  | { status: "not-found"; error: unknown }
  | { status: "error"; error: unknown };

// The key owns a result. A key change discards old data immediately; same-key refreshes retain it visibly.
export function useRequest<T>(
  key: string,
  load: (signal: AbortSignal) => Promise<T>,
): [RequestState<T>, () => void] {
  const [version, setVersion] = useState(0);
  const [result, setResult] = useState<{
    key: string;
    state: RequestState<T>;
  } | null>(null);
  const sequence = useRef(0);

  useEffect(() => {
    const controller = new AbortController();
    const request = ++sequence.current;
    load(controller.signal)
      .then((data) => {
        if (request === sequence.current && !controller.signal.aborted) {
          setResult({
            key,
            state: {
              status: "ready",
              data,
              refreshing: false,
              refreshError: null,
            },
          });
        }
      })
      .catch((error: unknown) => {
        if (
          request !== sequence.current ||
          controller.signal.aborted ||
          isAbortError(error)
        )
          return;
        setResult((current) => {
          if (current?.key === key && current.state.status === "ready") {
            return {
              key,
              state: {
                ...current.state,
                refreshing: false,
                refreshError: error,
              },
            };
          }
          return {
            key,
            state:
              statusOf(error) === 404
                ? { status: "not-found", error }
                : { status: "error", error },
          };
        });
      });

    return () => controller.abort();
  }, [key, version]); // eslint-disable-line react-hooks/exhaustive-deps

  const reload = useCallback(() => {
    setResult((current) =>
      current?.key === key && current.state.status === "ready"
        ? {
            key,
            state: { ...current.state, refreshing: true, refreshError: null },
          }
        : current,
    );
    setVersion((current) => current + 1);
  }, [key]);
  return [result?.key === key ? result.state : { status: "loading" }, reload];
}
