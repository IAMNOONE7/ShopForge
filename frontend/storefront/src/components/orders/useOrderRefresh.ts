import { useCallback, useEffect, useState } from "react";
import type { Order } from "../../cart";
import { useRequest } from "../../useRequest";

const fastPollDelay = 3_000;
const slowPollDelay = 10_000;
const fastPollCount = 3;
const pollBudget = 120_000;

type PollState = {
  key: string;
  polls: number;
  elapsed: number;
  timedOut: boolean;
};

export function useOrderRefresh(
  key: string,
  load: (signal: AbortSignal) => Promise<Order>,
) {
  const request = useRequest(key, load);
  const reload = request.reload;
  const [storedPoll, setStoredPoll] = useState<PollState>(() =>
    initialPoll(key),
  );
  const [visible, setVisible] = useState(
    () =>
      typeof document === "undefined" ||
      document.visibilityState !== "hidden",
  );
  const poll = storedPoll.key === key ? storedPoll : initialPoll(key);

  useEffect(() => {
    function changed() {
      setVisible(document.visibilityState !== "hidden");
    }

    document.addEventListener("visibilitychange", changed);
    return () => document.removeEventListener("visibilitychange", changed);
  }, []);

  const ready = request.status === "ready";
  const waiting = ready && orderNeedsRefresh(request.data);
  const refreshing = ready && request.refreshing;
  const refreshError = ready ? request.refreshError : null;

  useEffect(() => {
    if (
      !ready ||
      !waiting ||
      refreshing ||
      refreshError !== null ||
      poll.timedOut ||
      !visible
    ) {
      return;
    }

    const preferredDelay =
      poll.polls < fastPollCount ? fastPollDelay : slowPollDelay;
    const remaining = pollBudget - poll.elapsed;
    const delay = Math.min(preferredDelay, remaining);
    const timer = window.setTimeout(() => {
      const elapsed = poll.elapsed + delay;
      if (
        remaining <= 0 ||
        delay < preferredDelay ||
        elapsed >= pollBudget
      ) {
        setStoredPoll({
          key,
          polls: poll.polls,
          elapsed,
          timedOut: true,
        });
        return;
      }

      setStoredPoll({
        key,
        polls: poll.polls + 1,
        elapsed,
        timedOut: false,
      });
      reload();
    }, Math.max(0, delay));

    return () => window.clearTimeout(timer);
  }, [
    key,
    poll,
    ready,
    refreshError,
    refreshing,
    reload,
    visible,
    waiting,
  ]);

  const refresh = useCallback(() => {
    setStoredPoll(initialPoll(key));
    reload();
  }, [key, reload]);

  return {
    request,
    waiting,
    timedOut: poll.timedOut,
    refresh,
  };
}

export function orderNeedsRefresh(order: Order) {
  if (order.status === "AwaitingPayment") return true;
  if (
    (order.status === "Paid" || order.status === "Shipped") &&
    !order.documents.some((document) => document.kind === "Invoice")
  ) {
    return true;
  }
  return (
    order.status === "Refunded" &&
    !order.documents.some((document) => document.kind === "CreditNote")
  );
}

function initialPoll(key: string): PollState {
  return { key, polls: 0, elapsed: 0, timedOut: false };
}
