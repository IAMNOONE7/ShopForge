import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { getProfile, type Customer } from "../account";
import { statusOf } from "../api/errors";
import { onUnauthorized } from "../api/http";
import { CustomerContext, type CustomerState } from "../customerContext";

export function CustomerProvider({ children }: { children: ReactNode }) {
  const generation = useRef(0), pendingRead = useRef<AbortController | null>(null);
  const invalidate = useCallback(() => { generation.current++; pendingRead.current?.abort(); pendingRead.current = null; }, []);
  const [version, setVersion] = useState(0);
  const [state, setState] = useState<Omit<CustomerState, "apply" | "retry">>({
    status: "checking",
    customer: null,
    error: null,
  });

  useEffect(
    () =>
      onUnauthorized(() => {
        invalidate(); setState({ status: "guest", customer: null, error: null });
      }),
    [invalidate],
  );

  useEffect(() => {
    const controller = new AbortController();
    const attempt = ++generation.current; pendingRead.current = controller;
    getProfile(controller.signal)
      .then((customer) => {
        if (!controller.signal.aborted && generation.current === attempt)
          setState({ status: "authenticated", customer, error: null });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || generation.current !== attempt) return;
        setState(
          statusOf(error) === 401
            ? { status: "guest", customer: null, error: null }
            : { status: "error", customer: null, error },
        );
      });
    return () => controller.abort();
  }, [version]);

  const apply = useCallback((customer: Customer | null) => {
    invalidate();
    setState({
      status: customer ? "authenticated" : "guest",
      customer,
      error: null,
    });
  }, [invalidate]);
  const retry = useCallback(() => {
    invalidate();
    setState({ status: "checking", customer: null, error: null });
    setVersion((current) => current + 1);
  }, [invalidate]);

  return (
    <CustomerContext value={{ ...state, apply, retry }}>
      {children}
    </CustomerContext>
  );
}
