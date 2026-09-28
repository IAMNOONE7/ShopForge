import { useCallback, useEffect, useRef, useState } from "react";
import { statusOf } from "./api/errors";
import { isAbortError } from "./api/http";

type StoredRequestState<T> =
  | { status: "loading" }
  | {
      status: "ready";
      data: T;
      refreshing: boolean;
      refreshError: unknown | null;
    }
  | { status: "not-found"; error: unknown }
  | { status: "error"; error: unknown };

export type RequestState<T> = StoredRequestState<T> & {
  reload: () => void;
  transitionData?: T;
};

type RequestOptions = {
  retainPrevious?: (previousKey: string) => boolean;
};

// The key owns a result. Key changes discard data unless a caller explicitly opts into same-scope transition data; same-key refreshes retain it visibly.
export function useRequest<T>(
  key: string,
  load: (signal: AbortSignal) => Promise<T>,
  options: RequestOptions = {},
): RequestState<T> {
  const [version, setVersion] = useState(0);
  const [result, setResult] = useState<{
    key: string;
    state: StoredRequestState<T>;
  } | null>(null);
  const sequence = useRef(0);
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

  const state: StoredRequestState<T> =
    result?.key === key ? result.state : { status: "loading" };
  const transitionData =
    result?.key !== key &&
    result?.state.status === "ready" &&
    options.retainPrevious?.(result.key)
      ? result.state.data
      : undefined;
  return { ...state, reload, transitionData };
}
