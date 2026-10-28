import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { planLimitRefusal, type PlanResource } from "../api/planUsage";

export function PlanLimitNotice({ error, resource }: { error: unknown; resource: PlanResource }) {
  const { t } = useTranslation("planUsage");
  const feedback = useRef<HTMLDivElement>(null);
  const refused = planLimitRefusal(error, resource);
  useEffect(() => { if (refused) feedback.current?.focus(); }, [error, refused]);
  if (!refused) return null;
  return <div ref={feedback} tabIndex={-1} className="request-error" role="alert"><p>{t(resource === "stores" ? "storesRefused" : "productsRefused")}</p><p>{t("draftKept")}</p><Link to="/plan">{t("reviewPlan")}</Link></div>;
}

export function PlanUsageHint({ resource }: { resource: PlanResource | "import" }) {
  const { t } = useTranslation("planUsage");
  return <p className="hint">{t(resource === "stores" ? "storesHint" : resource === "products" ? "productsHint" : "importHint")} <Link to="/plan">{t("reviewPlan")}</Link></p>;
}
