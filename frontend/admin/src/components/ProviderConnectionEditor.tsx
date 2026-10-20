import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { ConnectionInput, ProviderConnection } from "../api/providerConnections";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export type ConnectionEditor = { kind: "metadata" | "secret" | "disconnect"; provider: string; item: ProviderConnection | null };
export type ConnectionChange = { kind: "metadata"; input: ConnectionInput } | { kind: "secret"; secret: string } | { kind: "disconnect" };

export function ProviderConnectionEditor({ editor, disabled, pending, onSave, onCancel }: {
  editor: ConnectionEditor; disabled: boolean; pending: boolean; onSave: (change: ConnectionChange) => Promise<void>; onCancel: () => void;
}) {
  const { t } = useTranslation("connections");
  const { item, provider, kind } = editor;
  const [merchantId, setMerchantId] = useState(item?.merchantId ?? "");
  const [environment, setEnvironment] = useState(item?.environment === "Test" || item?.environment === "Live" ? item.environment : "");
  const [publishableKey, setPublishableKey] = useState(item?.publishableKey ?? "");
  const [isActive, setIsActive] = useState(item?.isActive ?? true);
  const [secret, setSecret] = useState("");
  const [verified, setVerified] = useState(false);
  const [invalid, setInvalid] = useState<string[]>([]);
  const title = useRef<HTMLHeadingElement>(null);
  useEffect(() => { title.current?.focus(); }, []);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled || pending) return;
    if (kind === "disconnect") { if (verified) await onSave({ kind }); return; }
    const issues = kind === "secret" ? (!secret.trim() ? ["secret"] : [])
      : [!merchantId.trim() || merchantId.trim().length > 100 ? "merchantId" : "", !["Test", "Live"].includes(environment) ? "environment" : "", publishableKey.trim().length > 200 ? "publishableKey" : ""].filter(Boolean);
    setInvalid(issues);
    if (issues.length) { document.getElementById(`connection-${issues[0]}`)?.focus(); return; }
    if (kind === "metadata") await onSave({ kind, input: { merchantId: merchantId.trim(), environment: environment as "Test" | "Live", isActive: item ? isActive : true, publishableKey: publishableKey.trim() || null } });
    else {
      const value = secret; setSecret("");
      await onSave({ kind, secret: value });
    }
  }
  function error(field: "merchantId" | "environment" | "publishableKey" | "secret") { return invalid.includes(field) ? t(`validation.${field}`) : undefined; }
  return <form className="connection-editor" aria-labelledby="connection-editor-title" autoComplete="off" noValidate onSubmit={submit}
    onKeyDown={event => { if (event.key === "Escape" && !pending) { event.preventDefault(); onCancel(); } }}>
    <h2 id="connection-editor-title" ref={title} tabIndex={-1}>{t(`editor.${kind}`, { provider })}</h2>
    <fieldset className="connection-fields" disabled={disabled || pending}>
      <legend className="sr-only">{t(`editor.${kind}`, { provider })}</legend>
      {kind === "metadata" ? <>
        <Field id="connection-merchantId" name="merchantId" label={t(provider === "comgate" ? "comgateMerchant" : "accountReference")}
          hint={t(provider === "packeta" ? "packetaAccountHint" : "accountHint")} value={merchantId} error={error("merchantId")} autoComplete="off" onChange={event => setMerchantId(event.target.value)} />
        <div className="connection-select"><label htmlFor="connection-environment">{t("environment")}</label><select id="connection-environment" name="environment" value={environment}
          aria-describedby={`connection-environment-hint${error("environment") ? " connection-environment-error" : ""}`} aria-invalid={!!error("environment") || undefined} onChange={event => setEnvironment(event.target.value)}>
          <option value="" disabled>{t("chooseEnvironment")}</option><option value="Test">{t("test")}</option><option value="Live">{t("live")}</option>
        </select><p id="connection-environment-hint" className="hint">{t(provider === "packeta" ? "packetaEnvironmentHint" : "environmentHint")}</p>
          {error("environment") && <p id="connection-environment-error" className="field-error">{error("environment")}</p>}</div>
        {environment === "Live" && <p className="connection-live-note">{t("liveHint")}</p>}
        <Field id="connection-publishableKey" name="publishableKey" label={t(provider === "packeta" ? "mapKey" : "publicKey")} hint={t("publicKeyHint")} value={publishableKey} error={error("publishableKey")} autoComplete="off" onChange={event => setPublishableKey(event.target.value)} />
        {item ? <label className="listing-check"><input name="isActive" type="checkbox" checked={isActive} onChange={event => setIsActive(event.target.checked)} />{t("active")}</label> : <p className="hint">{t("newActive")}</p>}
        <p className="hint">{t("separateSaves")}</p>
      </> : kind === "secret" ? <>
        <p>{t("secretFor", { provider, merchant: item?.merchantId ?? "", environment: item?.environment ?? "" })}</p>
        <Field id="connection-secret" name="secret" type="password" autoComplete="new-password" autoCapitalize="none" spellCheck={false}
          label={t(provider === "packeta" ? "apiPassword" : "credential")} hint={t("secretHint")} value={secret} error={error("secret")} onChange={event => setSecret(event.target.value)} />
        <p className="hint">{t("rotation")}</p>
      </> : <>
        <p>{t("disconnectHint", { provider, merchant: item?.merchantId ?? "", environment: item?.environment ?? "" })}</p>
        <p className="hint">{t("disconnectEffect")}</p>
        <label className="listing-check"><input type="checkbox" checked={verified} onChange={event => setVerified(event.target.checked)} />{t("verifyDisconnect")}</label>
      </>}
    </fieldset>
    <div className="cluster"><Button type="submit" variant={kind === "disconnect" ? "danger" : "primary"} disabled={disabled || kind === "disconnect" && !verified} busy={pending} busyLabel={t("saving")}>{t(`save.${kind}`)}</Button>
      <Button type="button" variant="secondary" disabled={pending} onClick={onCancel}>{t("cancel")}</Button></div>
  </form>;
}
