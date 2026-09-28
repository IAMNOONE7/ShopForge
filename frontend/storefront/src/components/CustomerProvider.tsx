import { useCallback, useEffect, useState, type ReactNode } from "react";
import { getProfile, type Customer } from "../account";
import { statusOf } from "../api/errors";
import { onUnauthorized } from "../api/http";
import { CustomerContext, type CustomerState } from "../customerContext";

export function CustomerProvider({ children }: { children: ReactNode }) {
  const [version, setVersion] = useState(0);
  const [state, setState] = useState<Omit<CustomerState, "apply" | "retry">>({
    status: "checking",
    customer: null,
    error: null,
  });

  useEffect(
    () =>
      onUnauthorized(() =>
        setState({ status: "guest", customer: null, error: null }),
      ),
    [],
  );

  useEffect(() => {
    const controller = new AbortController();
    getProfile(controller.signal)
      .then((customer) => {
        if (!controller.signal.aborted)
          setState({ status: "authenticated", customer, error: null });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState(
          statusOf(error) === 401
            ? { status: "guest", customer: null, error: null }
            : { status: "error", customer: null, error },
        );
      });
    return () => controller.abort();
  }, [version]);

  const apply = useCallback((customer: Customer | null) => {
    setState({
      status: customer ? "authenticated" : "guest",
      customer,
      error: null,
    });
  }, []);
  const retry = useCallback(() => {
    setState({ status: "checking", customer: null, error: null });
    setVersion((current) => current + 1);
  }, []);

  return (
    <CustomerContext value={{ ...state, apply, retry }}>
      {children}
    </CustomerContext>
  );
}
