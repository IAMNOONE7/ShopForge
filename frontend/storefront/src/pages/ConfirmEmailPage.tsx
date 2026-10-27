import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import type { Customer } from "../account";
import { customerEmailApi } from "../api/customerEmail";
import { statusOf } from "../api/errors";
import type { EmailChangeEntry } from "../emailChangeEntry";
import { AuthLayout } from "../components/auth/AuthLayout";
import { Button } from "../components/ui/Button";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useCustomer } from "../customerContext";
import { useStore } from "../storeContext";
import "./CustomerEmail.css";

type Phase = "ready" | "invalid" | "conflict" | "success" | "uncertain";
export function ConfirmEmailPage({ entry }: { entry: EmailChangeEntry | null }) {
  const store = useStore();
  return <Confirmation key={store.id} entry={entry} />;
}
function Confirmation({ entry }: { entry: EmailChangeEntry | null }) {
  const { t } = useTranslation("customerEmail"), session = useCustomer();
  const [phase, setPhase] = useState<Phase>(() => entry?.available() ? "ready" : "invalid");
  const [action, setAction] = useState<"confirm" | "logout" | "read" | null>(null), [failure, setFailure] = useState<{ error: unknown; operation: "read" | "write" } | null>(null);
  const [confirmed, setConfirmed] = useState<Customer | null>(null), [sessionReview, setSessionReview] = useState(false), [sessionChanged, setSessionChanged] = useState(false);
  const [checked, setChecked] = useState<{ customer: Customer | null } | null>(null);
  const active = useRef(false), lock = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (phase !== "ready" || failure || checked || sessionChanged) feedback.current?.focus(); }, [phase, failure, checked, sessionChanged]);
  const owns = (request: AbortController) => active.current && !request.signal.aborted;
  async function readSession(request: AbortController) {
    try { return await customerEmailApi.profile(request.signal); }
    catch (error) { if (statusOf(error) === 401) return null; throw error; }
  }
  async function perform(kind: "confirm" | "logout" | "read") {
    if (lock.current || phase !== "ready" && kind !== "read" || kind === "confirm" && (!entry?.available() || session.status !== "guest")) return;
    lock.current = true; setAction(kind); setFailure(null); const request = new AbortController(); controller.current = request;
    let submitted = false, operation: "read" | "write" = "read";
    try {
      if (kind === "logout") {
        setSessionReview(true); operation = "write"; await customerEmailApi.logout(request.signal); operation = "read";
      }
      const current = await readSession(request);
      if (!owns(request)) return;
      if (kind !== "confirm") {
        session.apply(current); setSessionReview(false);
        if (phase === "uncertain") setChecked({ customer: current });
        else if (current) setSessionChanged(true);
        return;
      }
      if (current !== null) { session.apply(current); setSessionChanged(true); return; }
      const token = entry?.take(); if (!token) { setPhase("invalid"); return; }
      submitted = true; operation = "write";
      const customer = await customerEmailApi.confirm(token, request.signal);
      if (owns(request)) { setConfirmed(customer); session.apply(customer); setPhase("success"); }
    } catch (error) {
      if (!owns(request)) return;
      setFailure({ error, operation });
      if (submitted) setPhase(statusOf(error) === 400 ? "invalid" : statusOf(error) === 409 ? "conflict" : "uncertain");
    } finally { lock.current = false; if (active.current) setAction(null); }
  }
  const footer = <Link to="/account">{t("backAccount")}</Link>;
  if (phase === "invalid" || phase === "conflict") return <AuthLayout className="customer-email-page" title={t(phase === "invalid" ? "invalidTitle" : "conflictTitle")} footer={footer}>
    <div className="customer-email" ref={feedback} tabIndex={-1}><p>{t(phase === "invalid" ? "invalidBody" : "conflictBody")}</p><Link to="/account/email">{t("requestAnother")}</Link></div>
  </AuthLayout>;
  if (phase === "success") return <AuthLayout className="customer-email-page" title={t("confirmedTitle")} footer={footer}><div className="customer-email" ref={feedback} tabIndex={-1}><p role="status">{t("confirmedBody", { email: confirmed!.email })}</p></div></AuthLayout>;
  if (phase === "uncertain") return <AuthLayout className="customer-email-page" title={t("unconfirmedTitle")} footer={footer}>
    <div className="customer-email" ref={feedback} tabIndex={-1}><p>{t("confirmUncertain")}</p>
      {failure && <RequestError error={failure.error} operation={failure.operation} />}
      <Button type="button" busy={action === "read"} busyLabel={t("checkingSession")} onClick={() => void perform("read")}>{t("checkSession")}</Button>
      {checked && <><p role="status">{checked.customer ? t("currentSession", { email: checked.customer.email }) : t("guestSession")}</p><p>{t("readNotProof")}</p></>}
    </div>
  </AuthLayout>;
  return <AuthLayout className="customer-email-page" title={t("confirmTitle")} introduction={<p>{t("confirmIntro")}</p>} footer={footer}>
    <div className="customer-email">
      {session.status === "checking" ? <LoadingState label={t("checkingSession")} /> : session.status === "error" ? <RequestError error={session.error} operation="session" onRetry={session.retry} /> : <>
        {sessionChanged && <div ref={feedback} tabIndex={-1}><p role="status">{t("sessionChanged")}</p></div>}
        {session.status === "authenticated" && <><p>{t("signedInGate", { email: session.customer!.email })}</p><p>{t("signOutHint")}</p></>}
        {sessionReview ? <Button type="button" busy={action !== null} busyLabel={t("checkingSession")} onClick={() => void perform("read")}>{t("checkSession")}</Button>
          : session.status === "authenticated" ? <Button type="button" variant="secondary" busy={action !== null} busyLabel={t("signingOut")} onClick={() => void perform("logout")}>{t("signOutContinue")}</Button>
            : <Button type="button" busy={action !== null} busyLabel={t("confirming")} onClick={() => void perform("confirm")}>{t("confirmAction")}</Button>}
      </>}
      {failure && <div ref={feedback} tabIndex={-1}><RequestError error={failure.error} operation={failure.operation} />{failure.operation === "read" && <p>{t("sessionReadFailed")}</p>}</div>}
    </div>
  </AuthLayout>;
}
