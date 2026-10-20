import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { api, type PaymentMethod } from "../api";
import type { ProviderConnection } from "../api/providerConnections";
import { useMethodEditor } from "../useMethodEditor";
import type { RequestState } from "../useRequest";
import { MethodProviderNotice, MethodStatus } from "./MethodStatus";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export function PaymentMethodsSection({ storeId, allowed, storeStale, refreshToken, connections, onAttempt }: {
  storeId: string; allowed: boolean; storeStale: boolean; refreshToken: number; connections: RequestState<ProviderConnection[]>; onAttempt: () => void;
}) {
  const { t } = useTranslation(["methodSetup", "methods"]);
  const work = useMethodEditor({ storeId, kind: "payment", load: api.paymentMethods, loadProviders: api.paymentProviders, allowed, storeStale, refreshToken, onAttempt });
  const { headingRef } = work;
  return <section className="method-workspace" aria-labelledby="payment-methods-title"><div className="section-heading"><h2 id="payment-methods-title" ref={headingRef} tabIndex={-1}>{t("methods:payment")}</h2>
    {allowed && <Button type="button" disabled={work.disabled || !!work.editor || !work.providerReady || !work.keys.length} onClick={event => work.open(null, event.currentTarget)}>{t("methodSetup:newPayment")}</Button>}</div>
    <p className="hint">{t("methods:paymentHint")}</p><MethodStatus work={work} kind="payment" />
    {work.ready && !work.items.length && <p>{t("methodSetup:noPayment")}</p>}
    <ul className="method-cards">{work.items.map(method => <li key={method.code}><div className="section-heading"><h3>{method.name}</h3><span className="chip">{t(method.isActive ? "methodSetup:active" : "methodSetup:inactive")}</span></div>
      <p className="hint"><code>{method.code}</code> · {method.providerKey}</p><MethodProviderNotice storeId={storeId} provider={method.providerKey} offered={work.providerReady ? work.keys.includes(method.providerKey) : null} connections={connections} />
      {allowed && <Button type="button" variant="secondary" aria-label={t("methodSetup:editPaymentNamed", { name: method.name })} disabled={work.disabled || !!work.editor} onClick={event => work.open(method, event.currentTarget)}>{t("methodSetup:edit")}</Button>}
    </li>)}</ul>
    {work.editor && <PaymentMethodForm method={work.editor.item} providers={work.keys} providerReady={work.providerReady} disabled={work.editorDisabled} pending={work.pending} storeId={storeId} connections={connections}
      onCancel={work.close} onSave={input => work.save(() => work.editor!.item ? api.updatePaymentMethod(storeId, work.editor!.item.code, { name: input.name, isActive: input.isActive }) : api.createPaymentMethod(storeId, input.name, input.providerKey))} />}
  </section>;
}
function PaymentMethodForm({ method, providers, providerReady, disabled, pending, onSave, onCancel, storeId, connections }: {
  method: PaymentMethod | null; providers: string[]; providerReady: boolean; disabled: boolean; pending: boolean; storeId: string; connections: RequestState<ProviderConnection[]>;
  onSave: (input: { name: string; isActive: boolean; providerKey: string }) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation(["methodSetup", "common"]);
  const [name, setName] = useState(method?.name ?? ""), [providerKey, setProvider] = useState(method?.providerKey ?? ""), [isActive, setActive] = useState(method?.isActive ?? true);
  const [invalid, setInvalid] = useState<string | null>(null), heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled || pending) return;
    const field = !name.trim() || name.trim().length > 100 ? "name" : !method && !providers.includes(providerKey) ? "providerKey" : null;
    setInvalid(field); if (field) { document.getElementById(`payment-${field}`)?.focus(); return; }
    await onSave({ name: name.trim(), isActive, providerKey });
  }
  return <form className="method-editor" noValidate aria-busy={pending} aria-labelledby="payment-editor-title" onSubmit={submit} onKeyDown={event => { if (event.key === "Escape" && !pending) { event.preventDefault(); onCancel(); } }}>
    <h3 id="payment-editor-title" ref={heading} tabIndex={-1}>{t(method ? "methodSetup:editPayment" : "methodSetup:newPayment")}</h3><fieldset className="method-fields" disabled={disabled || pending}><legend className="sr-only">{t("methodSetup:paymentFields")}</legend>
      <Field id="payment-name" name="name" label={t("methodSetup:name")} hint={t("methodSetup:nameHint")} value={name} error={invalid === "name" ? t("methodSetup:invalidName") : undefined} onChange={event => setName(event.target.value)} />
      {method ? <p className="hint">{t("methodSetup:immutable", { code: method.code, provider: method.providerKey })}</p> : <div className="listing-field"><label htmlFor="payment-providerKey">{t("methodSetup:provider")}</label><select id="payment-providerKey" name="providerKey" value={providerKey} aria-describedby={invalid === "providerKey" ? "payment-provider-error" : undefined} aria-invalid={invalid === "providerKey" || undefined} onChange={event => setProvider(event.target.value)}>
        <option value="" disabled>{t("methodSetup:chooseProvider")}</option>{providers.map(key => <option key={key} value={key}>{key}</option>)}
      </select>{invalid === "providerKey" && <p className="field-error" id="payment-provider-error">{t("methodSetup:invalidProvider")}</p>}</div>}
      {providerKey && <MethodProviderNotice storeId={storeId} provider={providerKey} offered={providerReady ? providers.includes(providerKey) : null} connections={connections} />}
      {method ? <label className="listing-check"><input id="payment-isActive" name="isActive" type="checkbox" checked={isActive} onChange={event => setActive(event.target.checked)} />{t("methodSetup:active")}</label> : <p className="hint">{t("methodSetup:newActive")}</p>}
    </fieldset><div className="cluster"><Button type="submit" disabled={disabled} busy={pending} busyLabel={t("methodSetup:saving")}>{t("common:save")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("methodSetup:cancel")}</Button></div>
  </form>;
}
