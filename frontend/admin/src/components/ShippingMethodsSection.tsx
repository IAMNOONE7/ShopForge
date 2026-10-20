import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { api } from "../api";
import { fieldIssues } from "../api/errors";
import { isHttpError } from "../api/http";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { useTaxChoices } from "../useTaxChoices";
import { ShippingMethodForm, type ShippingInput } from "./ShippingMethodForm";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

const shippingFields = ["name", "price", "vatRate", "maxWeightGrams", "countries", "taxClass", "providerKey", "requiresPickupPoint", "isActive"];

export function ShippingMethodsSection({ storeId, money, allowed, storeStale }: {
  storeId: string; money: Intl.NumberFormat; allowed: boolean; storeStale: boolean;
}) {
  const { t } = useTranslation(["methods", "taxAssignments"]);
  const [state, reload] = useRequest(`shipping-methods:${storeId}`, async signal => ({ items: await api.shippingMethods(storeId, signal) }));
  const [providers, reloadProviders] = useRequest(`shipping-providers:${storeId}`, signal => api.shippingProviders(storeId, signal));
  const choices = useTaxChoices(storeId, 0, allowed);
  const [invalidated, setInvalidated] = useState<object | null>(null);
  const [target, setTarget] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [created, setCreated] = useState(0);
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(true);
  const result = useRef<HTMLDivElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const ready = state.status === "ready" && !state.refreshing && state.refreshError === null && state.data !== invalidated;
  const disabled = !allowed || storeStale || !ready || pending;
  const providerReady = providers.status === "ready" && !providers.refreshing && providers.refreshError === null;
  useEffect(() => { if (ready && target !== null && (saved || error !== null)) result.current?.focus(); }, [ready, target, saved, error]);
  async function save(code: string | null, input: ShippingInput) {
    if (disabled || state.status !== "ready" || code === null && !providerReady) return;
    setTarget(code ?? "new"); setSaved(false); setInvalidated(state.data);
    await run(async () => {
      try {
        if (code === null) await api.createShippingMethod(storeId, input);
        else {
          const { providerKey: _providerKey, ...details } = input;
          await api.updateShippingMethod(storeId, code, details);
        }
        if (active.current) { setSaved(true); if (code === null) setCreated(current => current + 1); }
      } finally {
        if (active.current) { reload(); choices.reload(); }
      }
    });
  }
  const readError = state.status === "error" || state.status === "not-found" ? state.error : state.status === "ready" ? state.refreshError : null;
  const providerError = providers.status === "error" || providers.status === "not-found" ? providers.error : providers.status === "ready" ? providers.refreshError : null;
  const refused = isHttpError(error) && error.kind === "http" && error.status !== null && [400, 401, 403, 404, 405, 415, 422, 429].includes(error.status);
  const errors = fieldIssues(error).flatMap(issue => {
    const field = shippingFields.find(field => field.toLowerCase() === issue.field.toLowerCase());
    return field ? [{ field: `shipping-${target}-${field}`, message: t("taxAssignments:serverField") }] : [];
  });
  return <section className="shipping-methods-section" aria-labelledby="shipping-methods-title">
    <h3 id="shipping-methods-title">{t("methods:shipping")}</h3>
    {state.status === "loading" && <LoadingState label={t("taxAssignments:loadingMethods")} lines={2} />}
    {readError !== null && <RequestError error={readError} operation="read" onRetry={reload} />}
    {providerError !== null && <RequestError error={providerError} operation="read" onRetry={reloadProviders} />}
    {storeStale && <InlineMessage>{t("taxAssignments:storeStale")}</InlineMessage>}
    {state.status === "ready" && !ready && <p role="status">{t("taxAssignments:readRequired")}</p>}
    {(saved || error !== null) && <div className="shipping-method-result" ref={result} tabIndex={-1}>
      {saved && <InlineMessage tone="success">{t("taxAssignments:saved")}</InlineMessage>}
      {error !== null && <><RequestError error={error} operation="write" fieldErrors={errors} /><p className="hint">{t(refused ? "taxAssignments:writeRefused" : "taxAssignments:writeFailed")}</p></>}
    </div>}
    {state.status === "ready" && <div className="shipping-method-list">{state.data.items.map(method => <ShippingMethodForm key={`${method.code}:${JSON.stringify(method)}`} method={method}
      providers={[]} choices={choices} disabled={disabled} pending={pending && target === method.code} money={money} onSave={input => save(method.code, input)} />)}
      <ShippingMethodForm key={`new:${created}`} providers={providers.status === "ready" ? providers.data : []} choices={choices} disabled={disabled || !providerReady}
        pending={pending && target === "new"} money={money} onSave={input => save(null, input)} />
    </div>}
  </section>;
}
