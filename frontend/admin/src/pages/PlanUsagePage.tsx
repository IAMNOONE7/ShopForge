import { useEffect, useEffectEvent, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { isHttpError } from "../api/http";
import { planUsageApi, type PlanUsage } from "../api/planUsage";
import { sameStaffAccount } from "../api/staffAccount";
import { Button } from "../components/ui/Button";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { useSession } from "../session";
import "./PlanUsagePage.css";

type View = { kind: "loading" } | { kind: "ready"; data: PlanUsage } | { kind: "failed"; error: unknown } | { kind: "closed"; reason: "expired" | "changed" | "forbidden" };
class SessionChanged extends Error {}

export function PlanUsagePage() {
  const { user } = useSession();
  return <PlanUsageWorkspace key={JSON.stringify([user.id, user.tenantId, user.role, user.email.trim().toLowerCase()])} />;
}

function PlanUsageWorkspace() {
  const { user } = useSession();
  const { t, i18n } = useTranslation("planUsage");
  const [view, setView] = useState<View>({ kind: "loading" });
  const [refreshed, setRefreshed] = useState(false);
  const active = useRef(false);
  const controller = useRef<AbortController | null>(null);
  const feedback = useRef<HTMLDivElement>(null);
  const owns = (request: AbortController) => active.current && controller.current === request && !request.signal.aborted;
  async function checkSession(request: AbortController) {
    const actual = await planUsageApi.session(request.signal);
    if (!owns(request)) return false;
    if (!sameStaffAccount(user, actual)) throw new SessionChanged();
    return true;
  }
  async function snapshot(request: AbortController) {
    if (!await checkSession(request)) return null;
    const data = await planUsageApi.read(request.signal);
    if (!owns(request) || !await checkSession(request)) return null;
    return data;
  }
  function load(request: AbortController, notify: boolean) {
    void snapshot(request)
      .then(data => { if (data && owns(request)) { setView({ kind: "ready", data }); setRefreshed(notify); } })
      .catch(error => {
        if (!owns(request)) return;
        if (error instanceof SessionChanged) setView({ kind: "closed", reason: "changed" });
        else if (isHttpError(error) && error.kind === "http" && (error.status === 401 || error.status === 403)) setView({ kind: "closed", reason: error.status === 401 ? "expired" : "forbidden" });
        else setView({ kind: "failed", error });
      })
      .finally(() => { if (controller.current === request) controller.current = null; });
  }
  const loadInitially = useEffectEvent((request: AbortController) => load(request, false));
  useEffect(() => {
    active.current = true;
    const request = new AbortController(); controller.current = request; loadInitially(request);
    return () => { active.current = false; controller.current?.abort(); controller.current = null; };
  }, []);
  useEffect(() => { if (refreshed || view.kind === "failed" || view.kind === "closed") feedback.current?.focus(); }, [refreshed, view.kind]);
  function refresh() {
    if (controller.current || view.kind === "closed") return;
    const request = new AbortController(); controller.current = request; setView({ kind: "loading" }); setRefreshed(false); load(request, true);
  }
  const format = (value: number) => new Intl.NumberFormat(i18n.resolvedLanguage).format(value);
  const data = view.kind === "ready" ? view.data : null;
  const extra = data?.usage.filter(row => row.name !== "stores" && row.name !== "products") ?? [];
  return <div className="plan-usage-page">
    <div className="plan-usage-heading"><h1>{t("title")}</h1><Button type="button" variant="secondary" busy={view.kind === "loading"} busyLabel={t("loading")} disabled={view.kind === "closed"} onClick={refresh}>{t("refresh")}</Button></div>
    <p>{t("scope")}</p><p className="hint">{t("snapshotHint")}</p>
    {view.kind === "loading" && <LoadingState label={t("loading")} />}
    <div ref={feedback} tabIndex={-1} className="plan-usage-feedback">
      {refreshed && view.kind === "ready" && <p role="status">{t("refreshed")}</p>}
      {view.kind === "failed" && <RequestError error={view.error} operation="read" onRetry={refresh} />}
      {view.kind === "closed" && <><p role="alert">{t(view.reason)}</p><a href={`${import.meta.env.BASE_URL}plan`}>{t("reload")}</a></>}
    </div>
    {data && <>
      <section className="plan-usage-panel" aria-labelledby="current-plan-title"><h2 id="current-plan-title">{t("currentPlan")}</h2><dl className="plan-usage-facts"><div><dt>{t("name")}</dt><dd>{data.name ?? t("notSupplied")}</dd></div><div><dt>{t("code")}</dt><dd>{data.code === null ? t("notSupplied") : <code>{data.code}</code>}</dd></div></dl><p className="hint">{t("assignmentHint")}</p></section>
      <div className="plan-usage-grid">{(["stores", "products"] as const).map(resource => {
        const used = data.usage.find(row => row.name === resource)?.value;
        const cap = resource === "stores" ? data.maxStores : data.maxProducts;
        const state = cap === null ? "uncapped" : used === undefined ? "unknown" : used > cap ? "over" : used === cap ? "at" : "under";
        return <section key={resource} className="plan-usage-panel" aria-labelledby={`plan-${resource}-title`}><h2 id={`plan-${resource}-title`}>{t(resource)}</h2><p>{t(resource === "stores" ? "storesMeaning" : "productsMeaning")}</p><dl className="plan-usage-facts"><div><dt>{t("used")}</dt><dd>{used === undefined ? t("notReported") : format(used)}</dd></div><div><dt>{t("cap")}</dt><dd>{cap === null ? t("noCap") : format(cap)}</dd></div></dl><p className={`plan-cap-state plan-cap-${state}`}>{t(state)}</p>{cap !== null && used !== undefined && used < cap && <p>{t("remaining", { amount: format(cap - used) })}</p>}<Link to={resource === "stores" ? "/stores" : "/products"}>{t(resource === "stores" ? "viewStores" : "viewProducts")}</Link></section>;
      })}</div>
      <p>{t("overHint")}</p>
      <section className="plan-usage-panel" aria-labelledby="other-usage-title"><h2 id="other-usage-title">{t("otherUsage")}</h2><p className="hint">{t("otherUsageHint")}</p>{extra.length === 0 ? <p>{t("noOtherUsage")}</p> : <dl className="plan-usage-facts">{extra.map(row => <div key={row.name}><dt>{row.name === "users" ? t("users") : row.name === "listings" ? t("listings") : <code>{row.name}</code>}</dt><dd>{format(row.value)}</dd></div>)}</dl>}{extra.some(row => row.name === "users") && <p className="hint">{t("usersMeaning")}</p>}{extra.some(row => row.name === "listings") && <p className="hint">{t("listingsMeaning")}</p>}</section>
    </>}
  </div>;
}
