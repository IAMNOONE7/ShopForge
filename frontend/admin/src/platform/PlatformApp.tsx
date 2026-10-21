import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, Navigate, Route, Routes } from "react-router";
import { platformApi, type PlatformUser } from "../api/platform";
import { statusOf } from "../api/errors";
import { onUnauthorized } from "../api/http";
import { Button } from "../components/ui/Button";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { adminHomePath } from "../routing";
import { PlatformLogin } from "./PlatformLogin";
import { TenantsPage } from "./TenantsPage";
import { operatorHomePath } from "./routing";
import "./platform.css";

type Session = { status: "checking" } | { status: "error"; error: unknown } | { status: "signed-out"; expired: boolean } | { status: "signed-in"; user: PlatformUser };
export default function PlatformApp() {
  const { t, i18n } = useTranslation(["operator", "navigation"]);
  const [session, setSession] = useState<Session>({ status: "checking" }), [version, setVersion] = useState(0);
  const [logoutError, setLogoutError] = useState<unknown | null>(null), [logoutPending, setLogoutPending] = useState(false);
  const active = useRef(true), lock = useRef(false), boot = useRef<AbortController | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => { document.title = t(session.status === "signed-in" ? "title" : "signInTitle"); }, [session.status, i18n.resolvedLanguage, t]);
  useEffect(() => onUnauthorized(() => { boot.current?.abort(); setLogoutError(null); setSession({ status: "signed-out", expired: true }); }, "platform"), []);
  useEffect(() => {
    const controller = new AbortController(); boot.current = controller;
    platformApi.me(controller.signal).then(user => { if (!controller.signal.aborted) setSession({ status: "signed-in", user }); })
      .catch(error => { if (!controller.signal.aborted) setSession(statusOf(error) === 401 ? { status: "signed-out", expired: false } : { status: "error", error }); });
    return () => controller.abort();
  }, [version]);
  function checkSession() { setLogoutError(null); setSession({ status: "checking" }); setVersion(current => current + 1); }
  async function logout() {
    if (lock.current) return; lock.current = true; setLogoutPending(true); setLogoutError(null);
    try { await platformApi.logout(); if (active.current) { window.history.replaceState(null, "", operatorHomePath()); setSession({ status: "signed-out", expired: false }); } }
    catch (error) { if (active.current) setLogoutError(error); }
    finally { lock.current = false; if (active.current) setLogoutPending(false); }
  }
  return <div className="operator-frame"><a className="skip-link" href="#operator-main">{t("navigation:skipToContent")}</a>
    <header className="admin-header"><div className="admin-header-inner container"><Link className="admin-brand" to="/platform/tenants">{t("title")}</Link>
      {session.status === "signed-in" && <div className="admin-user"><span><strong>{session.user.email}</strong><span>{t("operatorRole")}</span></span>
        <Button type="button" busy={logoutPending} onClick={() => void logout()}>{t("navigation:signOut")}</Button></div>}
    </div></header>
    {session.status === "checking" ? <main id="operator-main" tabIndex={-1} className="app container app-boot"><LoadingState label={t("checking")} lines={3} /></main>
      : session.status === "error" ? <main id="operator-main" tabIndex={-1} className="app container"><h1>{t("sessionUnavailable")}</h1><RequestError error={session.error} operation="session" onRetry={checkSession} /></main>
      : session.status === "signed-out" ? <PlatformLogin expired={session.expired} onCheckSession={checkSession} onSignedIn={user => { setLogoutError(null); setSession({ status: "signed-in", user }); }} />
      : <main id="operator-main" className="app container operator-main" tabIndex={-1}>
        {logoutError !== null && <div className="operator-session-error"><RequestError error={logoutError} operation="session" /><p>{t("logoutUnknown")}</p><Button type="button" onClick={checkSession}>{t("checkSession")}</Button></div>}
        <Routes><Route index element={<Navigate to="tenants" replace />} /><Route path="tenants" element={<TenantsPage key={session.user.id} userId={session.user.id} sessionStale={logoutPending || logoutError !== null} />} />
          <Route path="*" element={<><h1>{t("notFound")}</h1><Link to="/platform/tenants">{t("companies")}</Link></>} /></Routes>
      </main>}
    <footer className="operator-footer container"><a href={adminHomePath()}>{t("companyAdmin")}</a><p>{t("separateSignIn")}</p></footer>
  </div>;
}
