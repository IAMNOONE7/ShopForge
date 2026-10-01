import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router";
import { signOut, type Customer } from "../account";
import { AccountOrderHistory } from "../components/account/AccountOrderHistory";
import { AccountProfile } from "../components/account/AccountProfile";
import { Button } from "../components/ui/Button";
import { EmptyState } from "../components/ui/EmptyState";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";

export function AccountPage() {
  const { t } = useTranslation(["account", "auth", "common"]);
  const customerState = useCustomer();

  if (customerState.status === "checking") {
    return (
      <section className="account-load-state">
        <h1>{t("account:title")}</h1>
        <LoadingState label={t("account:loading")} lines={5} />
      </section>
    );
  }

  if (customerState.status === "error") {
    return (
      <section className="account-load-state">
        <h1>{t("account:title")}</h1>
        <RequestError
          error={customerState.error}
          operation="session"
          onRetry={customerState.retry}
        />
      </section>
    );
  }

  if (customerState.status === "guest" || !customerState.customer) {
    return (
      <EmptyState
        title={t("account:title")}
        action={
          <Link
            to="/account/sign-in?returnTo=%2Faccount"
            className="button"
          >
            {t("auth:signIn")}
          </Link>
        }
      >
        <p>{t("account:signInPrompt")}</p>
      </EmptyState>
    );
  }

  return (
    <AuthenticatedAccount
      customer={customerState.customer}
      apply={customerState.apply}
    />
  );
}

function AuthenticatedAccount({
  customer,
  apply,
}: {
  customer: Customer;
  apply: (customer: Customer | null) => void;
}) {
  const { t } = useTranslation(["account", "navigation"]);
  const navigate = useNavigate();
  const [signOutError, setSignOutError] = useState<unknown | null>(null);
  const [signOutPending, setSignOutPending] = useState(false);
  const signOutLock = useRef(false);

  async function leave() {
    if (signOutLock.current) return;
    signOutLock.current = true;
    setSignOutPending(true);
    setSignOutError(null);
    try {
      await signOut();
      apply(null);
      void navigate("/");
    } catch (error) {
      setSignOutError(error);
    } finally {
      setSignOutPending(false);
      signOutLock.current = false;
    }
  }

  return (
    <div className="account">
      <header className="account-header">
        <div>
          <h1>{t("account:hello", { name: customer.firstName })}</h1>
          <p>{t("account:signedInAs", { email: customer.email })}</p>
        </div>
        <Button
          type="button"
          variant="secondary"
          busy={signOutPending}
          busyLabel={t("account:signingOut")}
          onClick={() => void leave()}
        >
          {t("navigation:signOut")}
        </Button>
      </header>
      {signOutError !== null && (
        <RequestError error={signOutError} operation="write" />
      )}
      <div className="account-layout">
        <AccountProfile customer={customer} onUpdated={apply} />
        <AccountOrderHistory customerKey={customer.email} />
      </div>
    </div>
  );
}
