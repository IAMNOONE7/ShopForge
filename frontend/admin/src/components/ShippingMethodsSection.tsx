import { lazy, useState } from "react";
import { parcelShippingApi } from "../api/parcelShipping";
import { ShippingRateCardForm } from "./ShippingRateCardForm";
import { DeferredPage } from "./DeferredPage";
import "./ParcelShipping.css";
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

const ShippingQuotePanel = lazy(() => import("./ShippingQuotePanel").then(parts => ({ default: parts.ShippingQuotePanel })));

export function ShippingMethodsSection({ storeId, money, allowed, storeStale, refreshToken = 0, connections, onAttempt }: {
  storeId: string; money: Intl.NumberFormat; allowed: boolean; storeStale: boolean; refreshToken?: number;
  connections?: RequestState<ProviderConnection[]>; onAttempt?: () => void;
}) {
  const { t } = useTranslation(["methods", "methodSetup", "taxAssignments", "parcelShipping"]);
  const [termsDirty, setTermsDirty] = useState(false), [ratesDirty, setRatesDirty] = useState(false), [quoteCode, setQuoteCode] = useState<string | null>(null);
  const choices = useTaxChoices(storeId, 0, allowed);
  const work = useMethodEditor({ storeId, kind: "shipping", load: api.shippingMethods, loadProviders: api.shippingProviders, allowed, storeStale, refreshToken,
    onAttempt: () => { choices.reload(); onAttempt?.(); } });
  const launchProviders = work.keys.filter(key => key === "packeta");
  const active = work.items.filter(method => method.isActive);
  const nonLaunch = active.filter(method => method.providerKey !== "packeta");
  const modes = active.filter(method => method.providerKey === "packeta" && method.pricing !== "perParcel");
  const { headingRef } = work;
  return <section className="method-workspace shipping-methods-section" aria-labelledby="shipping-methods-title">
    <div className="section-heading"><h2 id="shipping-methods-title" ref={headingRef} tabIndex={-1}>{t("methods:shipping")}</h2>
      {allowed && <Button type="button" disabled={work.disabled || !!work.editor || !work.providerReady || !launchProviders.length} onClick={event => { setTermsDirty(false); setRatesDirty(false); work.open(null, event.currentTarget); }}>{t("methodSetup:newShipping")}</Button>}</div>
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
      <p><strong>{method.pricing === "perParcel" ? method.carrierService : t(method.providerKey === "packeta" ? method.requiresPickupPoint ? "methodSetup:zbox" : "methodSetup:address" : "methodSetup:legacyShipping")}</strong></p>
      <dl className="method-facts"><div><dt>{t("methods:price")}</dt><dd>{method.pricing === "perParcel" ? t("parcelShipping:perParcel") : money.format(method.price)}</dd></div><div><dt>{t("methods:vatPercent")}</dt><dd>{method.vatRate}%</dd></div>
        <div><dt>{t("taxAssignments:label")}</dt><dd>{method.taxClass ?? t("taxAssignments:fallback")}</dd></div>{method.pricing !== "perParcel" && <div><dt>{t("methods:maxWeight")}</dt><dd>{method.maxWeightGrams ?? t("methods:noLimit")}</dd></div>}
        <div><dt>{t("methods:countries")}</dt><dd>{method.countries.join(" · ") || t("methods:everywhere")}</dd></div></dl>
      {method.pricing === "perParcel" && <div className="parcel-rate-summary"><p>{t("parcelShipping:checkoutWait")}</p><p>{t("parcelShipping:parcelMaxWeightGrams")}: {method.parcelMaxWeightGrams ?? t("parcelShipping:serviceLimit")}</p><p>{t("parcelShipping:largest")}: {method.parcelLengthMm == null ? t("parcelShipping:serviceLimit") : [method.parcelLengthMm, method.parcelWidthMm, method.parcelHeightMm].join(" × ")}</p><p>{t("parcelShipping:service")}: <code>{method.carrierService}</code></p><p>{t("parcelShipping:source")}: {method.ratesFrom ? t(`parcelShipping:${method.ratesFrom}`) : t("parcelShipping:noPrices")}{method.ratesPricedOn ? " · " + method.ratesPricedOn : ""}</p>{(!method.ratesFrom || method.ratesFrom === "placeholder") && <p>{t("parcelShipping:placeholderWarning")}</p>}<ul>{method.rateBands?.map(band => <li key={band.upToGrams}>{band.upToGrams} g · {money.format(band.price)}</li>)}</ul></div>}
      <MethodProviderNotice storeId={storeId} provider={method.providerKey} offered={work.providerReady ? work.keys.includes(method.providerKey) : null} connections={connections} />
      {method.providerKey === "packeta" && method.requiresPickupPoint && (method.countries.length !== 1 || method.countries[0] !== "CZ") && <p className="hint">{t("methodSetup:zboxCountries")}</p>}
      {allowed && <Button type="button" variant="secondary" aria-label={t("methodSetup:editShippingNamed", { name: method.name })} disabled={work.disabled || !!work.editor} onClick={event => { setTermsDirty(false); setRatesDirty(false); work.open(method, event.currentTarget); }}>{t("methodSetup:edit")}</Button>}
      <Button type="button" data-read-action variant="secondary" disabled={!work.ready || storeStale || work.pending || !!work.editor} onClick={() => setQuoteCode(method.code)}>{t("parcelShipping:tryNamed", { name: method.name })}</Button>
    </li>)}</ul>
    {work.editor && <><ShippingMethodForm storeId={storeId} onDirty={setTermsDirty} method={work.editor.item ?? undefined} providers={launchProviders} choices={choices} disabled={work.editorDisabled || ratesDirty} pending={work.pending} money={money} onCancel={work.close}
      onSave={input => work.save(signal => {
        if (!work.editor!.item) return input.pricing === "perParcel" ? api.createShippingMethod(storeId, input, signal) : api.createShippingMethod(storeId, input);
        const { providerKey: _providerKey, ...details } = input;
        return work.editor!.item.pricing === "perParcel" || details.pricing === "perParcel" ? api.updateShippingMethod(storeId, work.editor!.item.code, details, signal) : api.updateShippingMethod(storeId, work.editor!.item.code, details);
      })} />
      {work.editor.item?.pricing === "perParcel" && <ShippingRateCardForm key={work.editor.item.code} method={work.editor.item} money={money} disabled={work.editorDisabled || termsDirty} pending={work.pending} onDirty={setRatesDirty} onSave={card => work.save(signal => parcelShippingApi.rates(storeId, work.editor!.item!, card, signal))} />}
      {!work.editor.item && <p>{t("parcelShipping:createFirst")}</p>}
    </>}
    {quoteCode && !work.editor && work.items.some(item => item.code === quoteCode) && <DeferredPage><ShippingQuotePanel key={JSON.stringify(work.items.find(item => item.code === quoteCode))} storeId={storeId} method={work.items.find(item => item.code === quoteCode)!} money={money} stale={!work.ready || storeStale} onClose={() => { setQuoteCode(null); document.getElementById("shipping-methods-title")?.focus(); }} /></DeferredPage>}
  </section>;
}
