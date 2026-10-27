import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { storeExportsApi, type StoreExportKind } from "../api/storeExports";
import { saveDownload } from "../api/http";
import { Button } from "../components/ui/Button";
import { RequestError } from "../components/ui/RequestError";
import { canManageCatalog, canManageStore, useSession } from "../session";
import { ForbiddenPage } from "./RouteStatePage";
import "./StoreExportsPage.css";

type Outcome = { kind: StoreExportKind; filename: string } | { kind: StoreExportKind; error: unknown } | { kind: StoreExportKind; canceled: true };
export function StoreExportsPage() {
  const { user } = useSession(), { store } = useSelectedStore();
  if (!canManageCatalog(user.role)) return <ForbiddenPage />;
  return <ExportsWorkspace key={[user.tenantId, user.id, user.role, JSON.stringify(store)].join(":")} ordersAllowed={canManageStore(user.role)} />;
}
function ExportsWorkspace({ ordersAllowed }: { ordersAllowed: boolean }) {
  const { t } = useTranslation("exports"), { store, storeRefreshing, storeRefreshError } = useSelectedStore();
  const [pending, setPending] = useState<StoreExportKind | null>(null), [outcome, setOutcome] = useState<Outcome | null>(null);
  const active = useRef(false), controller = useRef<AbortController | null>(null), currentKind = useRef<StoreExportKind | null>(null), feedback = useRef<HTMLDivElement>(null);
  const stale = storeRefreshing || storeRefreshError !== null, blocked = useRef(stale);
  useEffect(() => { active.current = true; const abort = () => controller.current?.abort(); return () => { active.current = false; abort(); }; }, []);
  useLayoutEffect(() => { blocked.current = stale; if (stale) controller.current?.abort(); }, [stale]);
  useEffect(() => { if (outcome) feedback.current?.focus(); }, [outcome]);
  function cancel() {
    const kind = currentKind.current;
    controller.current?.abort(); controller.current = null; currentKind.current = null;
    setPending(null); if (kind) setOutcome({ kind, canceled: true });
  }
  async function download(kind: StoreExportKind) {
    if (controller.current || blocked.current || kind === "orders" && !ordersAllowed) return;
    const request = new AbortController(); controller.current = request; currentKind.current = kind; setPending(kind); setOutcome(null);
    try {
      const file = await storeExportsApi.read(store.id, kind, request.signal);
      if (active.current && controller.current === request && !request.signal.aborted && !blocked.current) {
        saveDownload(file); setOutcome({ kind, filename: file.filename });
      }
    } catch (error) {
      if (active.current && controller.current === request && !request.signal.aborted) setOutcome({ kind, error });
    } finally {
      if (active.current && controller.current === request) {
        controller.current = null; currentKind.current = null; setPending(null);
        if (request.signal.aborted) setOutcome({ kind, canceled: true });
      }
    }
  }
  return <div className="exports-page">
    <Link to={"/stores/" + store.id}>{t("back")}</Link><h1>{t("title")}</h1><p>{t("scope", { store: store.name })}</p>
    <p>{t("intro")}</p><p className="exports-limit-note">{t("completeness")}</p>
    {stale && <p role="status">{t("storeStale")}</p>}
    <div className="exports-grid">
      <section className="exports-card" aria-labelledby="catalog-export-title"><h2 id="catalog-export-title">{t("catalog")}</h2>
        <p>{t("catalogScope")}</p><p>{t("catalogLimit")}</p>
        <details><summary>{t("catalogContents")}</summary><div className="export-guide"><p>{t("catalogColumns")}</p><p>{t("catalogStock")}</p><p>{t("catalogAxes")}</p><p>{t("catalogBackup")}</p><p>{t("importLimit")}</p></div></details>
        <Button type="button" disabled={stale || pending !== null} busy={pending === "catalog"} busyLabel={t("preparingCatalog")} onClick={() => void download("catalog")}>{t("downloadCatalog")}</Button>
        <div className="cluster"><Link to={"/stores/" + store.id + "/products"}>{t("catalogLink")}</Link><Link to={"/stores/" + store.id + "/import"}>{t("importLink")}</Link></div>
      </section>
      <section className="exports-card" aria-labelledby="orders-export-title"><h2 id="orders-export-title">{t("orders")}</h2>
        <p>{t("ordersScope")}</p><p>{t("ordersLimit")}</p>
        <details><summary>{t("ordersContents")}</summary><div className="export-guide"><p>{t("ordersColumns")}</p><p>{t("ordersCredit")}</p></div></details>
        {ordersAllowed ? <Button type="button" disabled={stale || pending !== null} busy={pending === "orders"} busyLabel={t("preparingOrders")} onClick={() => void download("orders")}>{t("downloadOrders")}</Button> : <p>{t("ordersPermission")}</p>}
        <Link to={"/stores/" + store.id + "/orders"}>{t("ordersLink")}</Link>
      </section>
    </div>
    {pending && <div className="exports-pending"><p role="status">{t(pending === "catalog" ? "preparingCatalog" : "preparingOrders")}</p><Button type="button" variant="secondary" onClick={cancel}>{t("cancel")}</Button></div>}
    {outcome && <div ref={feedback} tabIndex={-1} className="export-feedback"><h2>{t(outcome.kind === "catalog" ? "catalog" : "orders")}</h2>
      {"error" in outcome ? <RequestError error={outcome.error} operation="download" /> : <p role="status">{"filename" in outcome ? t("ready", { filename: outcome.filename }) : t("canceled")}</p>}
    </div>}
  </div>;
}
