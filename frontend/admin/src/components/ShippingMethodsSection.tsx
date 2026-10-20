import { useTranslation } from "react-i18next";
import { api } from "../api";
import type { ProviderConnection } from "../api/providerConnections";
import { useMethodEditor } from "../useMethodEditor";
import type { RequestState } from "../useRequest";
import { useTaxChoices } from "../useTaxChoices";
import { MethodProviderNotice, MethodStatus } from "./MethodStatus";
import { ShippingMethodForm } from "./ShippingMethodForm";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";

export function ShippingMethodsSection({ storeId, money, allowed, storeStale, refreshToken = 0, connections, onAttempt }: {
  storeId: string; money: Intl.NumberFormat; allowed: boolean; storeStale: boolean; refreshToken?: number;
  connections?: RequestState<ProviderConnection[]>; onAttempt?: () => void;
}) {
  const { t } = useTranslation(["methods", "methodSetup", "taxAssignments"]);
  const choices = useTaxChoices(storeId, 0, allowed);
  const work = useMethodEditor({ storeId, kind: "shipping", load: api.shippingMethods, loadProviders: api.shippingProviders, allowed, storeStale, refreshToken,
    onAttempt: () => { choices.reload(); onAttempt?.(); } });
  const launchProviders = work.keys.filter(key => key === "packeta");
  const active = work.items.filter(method => method.isActive);
  const nonLaunch = active.filter(method => method.providerKey !== "packeta");
  const modes = active.filter(method => method.providerKey === "packeta");
  const { headingRef } = work;
  return <section className="method-workspace shipping-methods-section" aria-labelledby="shipping-methods-title">
    <div className="section-heading"><h2 id="shipping-methods-title" ref={headingRef} tabIndex={-1}>{t("methods:shipping")}</h2>
      {allowed && <Button type="button" disabled={work.disabled || !!work.editor || !work.providerReady || !launchProviders.length} onClick={event => work.open(null, event.currentTarget)}>{t("methodSetup:newShipping")}</Button>}</div>
    <p className="hint">{t("methodSetup:shippingHint")}</p><MethodStatus work={work} kind="shipping" />
    {work.ready && <div className="launch-method-checks" aria-label={t("methodSetup:launchChecks")}>
      {nonLaunch.length > 0 && <InlineMessage>{t("methodSetup:nonLaunchActive", { names: nonLaunch.map(method => method.name).join(" · ") })}</InlineMessage>}
      <p>{t(nonLaunch.length ? "methodSetup:launchBlocked" : active.length ? "methodSetup:packetaOnly" : "methodSetup:noActiveShipping")}</p>
      <p className="hint">{t(modes.some(method => method.requiresPickupPoint) && modes.some(method => !method.requiresPickupPoint) ? "methodSetup:bothModes" : "methodSetup:reviewModes")}</p>
    </div>}
    {work.providerReady && !launchProviders.length && <p>{t("methodSetup:packetaNotOffered")}</p>}
    <ul className="method-cards">{work.items.map(method => <li key={method.code}>
      <div className="section-heading"><h3>{method.name}</h3><span className="chip">{t(method.isActive ? "methodSetup:active" : "methodSetup:inactive")}</span></div>
      <p className="hint"><code>{method.code}</code> · {method.providerKey}</p>
      <p><strong>{t(method.providerKey === "packeta" ? method.requiresPickupPoint ? "methodSetup:zbox" : "methodSetup:address" : "methodSetup:legacyShipping")}</strong></p>
      <dl className="method-facts"><div><dt>{t("methods:price")}</dt><dd>{money.format(method.price)}</dd></div><div><dt>{t("methods:vatPercent")}</dt><dd>{method.vatRate}%</dd></div>
        <div><dt>{t("taxAssignments:label")}</dt><dd>{method.taxClass ?? t("taxAssignments:fallback")}</dd></div><div><dt>{t("methods:maxWeight")}</dt><dd>{method.maxWeightGrams ?? t("methods:noLimit")}</dd></div>
        <div><dt>{t("methods:countries")}</dt><dd>{method.countries.join(" · ") || t("methods:everywhere")}</dd></div></dl>
      <MethodProviderNotice storeId={storeId} provider={method.providerKey} offered={work.providerReady ? work.keys.includes(method.providerKey) : null} connections={connections} />
      {method.providerKey === "packeta" && method.requiresPickupPoint && (method.countries.length !== 1 || method.countries[0] !== "CZ") && <p className="hint">{t("methodSetup:zboxCountries")}</p>}
      {allowed && <Button type="button" variant="secondary" aria-label={t("methodSetup:editShippingNamed", { name: method.name })} disabled={work.disabled || !!work.editor} onClick={event => work.open(method, event.currentTarget)}>{t("methodSetup:edit")}</Button>}
    </li>)}</ul>
    {work.editor && <ShippingMethodForm method={work.editor.item ?? undefined} providers={launchProviders} choices={choices} disabled={work.editorDisabled} pending={work.pending} money={money} onCancel={work.close}
      onSave={input => work.save(() => {
        if (!work.editor!.item) return api.createShippingMethod(storeId, input);
        const { providerKey: _providerKey, ...details } = input;
        return api.updateShippingMethod(storeId, work.editor!.item.code, details);
      })} />}
  </section>;
}
