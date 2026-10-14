import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { StoreReadinessCheck } from "../api";

type Definition = {
  label: "logo" | "company" | "product" | "payment" | "shipping" | "domain" | "paymentConnected" | "shippingConnected" | "tax" | "email" | "emailSender";
  destination?: "products" | "methods";
  heading?: "store-logo-title" | "store-details-title";
};
const definitions = new Map<string, Definition>([
  ["store.logo", { label: "logo", heading: "store-logo-title" }],
  ["store.company", { label: "company", heading: "store-details-title" }],
  ["store.primary-domain", { label: "domain" }],
  ["catalog.visible-products", { label: "product", destination: "products" }],
  ["catalog.tax-classes", { label: "tax" }],
  ["orders.payment-method", { label: "payment", destination: "methods" }],
  ["orders.shipping-method", { label: "shipping", destination: "methods" }],
  ["orders.payment-connected", { label: "paymentConnected", destination: "methods" }],
  ["orders.shipping-connected", { label: "shippingConnected", destination: "methods" }],
  ["platform.email", { label: "email" }],
  ["platform.email-sender", { label: "emailSender" }],
]);

export function StoreReadinessList({ storeId, checks, stale }: { storeId: string; checks: StoreReadinessCheck[]; stale: boolean }) {
  const { t } = useTranslation("stores");
  function focusHeading(id: string) {
    const heading = document.getElementById(id);
    if (!heading) return;
    heading.tabIndex = -1;
    heading.focus();
    heading.scrollIntoView({ block: "center", behavior: "smooth" });
  }
  return <div className="readiness-groups">
    {(["required", "advised"] as const).map((weight) => {
      const group = checks.filter((check) => check.weight === weight);
      return group.length > 0 && <section className="readiness-group" key={weight} aria-label={t(`readinessWeight.${weight}`)}>
        <h4>{t(`readinessWeight.${weight}`)}</h4>
        <p className="hint">{t(weight === "required" ? "requiredChecksHint" : "advisedChecksHint")}</p>
        <ul className="readiness-list">
          {group.map((check, index) => {
            const definition = definitions.get(check.check);
            const status = stale ? "unknown" : check.isReady ? "ready" : "missing";
            return <li key={`${check.check}:${index}`}>
              <span className={`readiness-icon readiness-${status}`} aria-hidden="true">{status === "ready" ? "✓" : status === "missing" ? "!" : "?"}</span>
              <div className="readiness-check-copy">
                <strong>{definition ? t(`checks.${definition.label}`) : t("unknownReadinessCheck", { check: check.check })}</strong>
                <small>{t(`checkStatus.${status}`)}</small>
                {!check.isReady && definition && <p className="hint">{t(`readinessHelp.${definition.label}`)}</p>}
                {check.problem && <details className="readiness-problem"><summary>{t("readinessServerDetail")}</summary><p>{check.problem}</p></details>}
                {!check.isReady && !definition && !check.problem && <p className="hint">{t("readinessProblemUnavailable")}</p>}
                {!check.isReady && definition?.destination && <Link to={`/stores/${storeId}/${definition.destination}`}>{t("fixCheck")}</Link>}
                {!check.isReady && definition?.heading && <button type="button" className="link-button" data-read-action onClick={() => focusHeading(definition.heading!)}>{t("fixCheck")}</button>}
              </div>
            </li>;
          })}
        </ul>
      </section>;
    })}
    {checks.length === 0 && <p className="hint">{t("readinessNoChecks")}</p>}
  </div>;
}
