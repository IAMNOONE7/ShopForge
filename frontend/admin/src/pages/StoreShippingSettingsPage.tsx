import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { isHttpError } from "../api/http";
import { statusOf } from "../api/errors";
import { shippingSettingsApi, shippingSettingFields, shippingSettingsDraft, parseShippingSetting, type ShippingSettings, type ShippingSettingsDraft, type ShippingSettingField } from "../api/shippingSettings";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useAction } from "../useAction";
import { useSetupProgress } from "../useSetupProgress";
import "./StoreShippingSettingsPage.css";

const hints = { packagingWeightGrams: "packagingWeightGramsHint", fillPercent: "fillPercentHint", largestParcelOrder: "largestParcelOrderHint" } as const;
const validation = { packagingWeightGrams: "packagingWeightGramsInvalid", fillPercent: "fillPercentInvalid", largestParcelOrder: "largestParcelOrderInvalid" } as const;
export function StoreShippingSettingsPage() {
  const { user } = useSession(), { store } = useSelectedStore();
  return <ShippingSettingsWorkspace key={[user.tenantId, user.id, user.role, store.id].join(":")} allowed={canManageStore(user.role)} />;
}
function ShippingSettingsWorkspace({ allowed }: { allowed: boolean }) {
  const { store, storeRefreshing, storeRefreshError } = useSelectedStore(), { t, i18n } = useTranslation(["shippingSettings", "auth", "navigation"]);
  const read = useSetupProgress("shipping-settings:" + store.id, signal => shippingSettingsApi.read(store.id, signal), () => false);
  const [editor, setEditor] = useState<{ saved: ShippingSettings; draft: ShippingSettingsDraft; storeVersion: string } | null>(null);
  const [issues, setIssues] = useState<ShippingSettingField[]>([]), [saved, setSaved] = useState(false), [failure, setFailure] = useState<{ error: unknown; uncertain: boolean } | null>(null);
  const [, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(false), controller = useRef<AbortController | null>(null), heading = useRef<HTMLHeadingElement>(null), opener = useRef<HTMLButtonElement>(null), summary = useRef<HTMLDivElement>(null), outcome = useRef<HTMLDivElement>(null), firstField = useRef<HTMLInputElement>(null);
  const editing = editor !== null;
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (editing) firstField.current?.focus(); }, [editing]);
  useEffect(() => { if (failure) outcome.current?.focus(); }, [failure]);
  const state = read.state, error = state.status === "ready" ? state.refreshError : state.status === "loading" ? null : state.error;
  const values = state.status === "ready" && ![401, 403, 404].includes(statusOf(error) ?? 0) ? state.data : null;
  const fresh = values !== null && state.status === "ready" && !state.refreshing && error === null && !read.suspended;
  const storeCurrent = !storeRefreshing && storeRefreshError === null;
  const changed = editor !== null && (JSON.stringify(editor.saved) !== JSON.stringify(values) || editor.storeVersion !== JSON.stringify(store));
  const disabled = !allowed || pending || !fresh || !storeCurrent || changed || failure?.uncertain === true;
  const fieldMessage = (field: ShippingSettingField) => issues.includes(field) || failure && isHttpError(failure.error) && Object.keys(failure.error.problem?.errors ?? {}).some(key => key.toLowerCase() === field.toLowerCase()) ? t(validation[field]) : undefined;
  function close() {
    if (pending) return; setEditor(null); setIssues([]); if (!failure?.uncertain) setFailure(null);
    window.requestAnimationFrame(() => { if (active.current) { if (opener.current && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); } });
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (disabled || !editor || !values || !read.isCurrent(values)) return;
    const invalid = shippingSettingFields.filter(field => parseShippingSetting(field, editor.draft[field]) === null); setIssues(invalid);
    if (invalid.length) { window.requestAnimationFrame(() => summary.current?.focus()); return; }
    const input = Object.fromEntries(shippingSettingFields.map(field => [field, parseShippingSetting(field, editor.draft[field])!])) as ShippingSettings;
    await run(async () => {
      read.suspend(); setFailure(null); setSaved(false); const request = new AbortController(); controller.current = request;
      try {
        const result = await shippingSettingsApi.save(store.id, input, request.signal);
        if (active.current && !request.signal.aborted) { read.accept(result); setEditor(null); setSaved(true); heading.current?.focus(); }
      } catch (error) {
        if (!active.current || request.signal.aborted) return;
        const refused = isHttpError(error) && error.kind === "http" && [400, 401, 403, 404, 409, 429].includes(error.status ?? 0);
        setFailure({ error, uncertain: !refused });
      } finally {
        controller.current = null; if (active.current && !request.signal.aborted) read.resume();
      }
    });
  }
  return <div className="shipping-settings-page">
    <Link to={"/stores/" + store.id}>{t("back")}</Link>
    <div className="shipping-settings-heading"><h1 ref={heading} tabIndex={-1}>{t("title")}</h1><Button type="button" variant="secondary" disabled={pending || read.refreshing} onClick={read.refresh}>{t("refresh")}</Button></div>
    <p>{t("scope", { store: store.name })}</p><p className="hint">{t("intro")}</p><p className="hint">{t("checkoutHint")}</p>
    {allowed && <Link to={"/stores/" + store.id + "/methods"}>{t("navigation:methods")}</Link>}
    {!allowed && <p>{t("auth:readOnly")}</p>}
    {read.refreshing && <LoadingState label={t("loading")} lines={3} />}
    {error !== null && <RequestError error={error} operation="read" onRetry={read.refresh} />}
    {!storeCurrent && <p role="status">{t("storeStale")}</p>}
    {values && <section className="shipping-settings-panel" aria-labelledby="shipping-settings-current">
      <h2 id="shipping-settings-current">{t("current")}</h2>
      {!fresh && !pending && <p role="status">{t("stale")}</p>}
      {saved && <p role="status">{t("saved")}</p>}
      <dl>{shippingSettingFields.map(field => <div key={field}><dt>{t(field)}</dt><dd>{new Intl.NumberFormat(i18n.resolvedLanguage).format(values[field])}{field === "packagingWeightGrams" ? " g" : field === "fillPercent" ? " %" : ""}</dd></div>)}</dl>
      {!editor && allowed && <Button ref={opener} type="button" disabled={disabled} onClick={() => {
        if (disabled || !read.isCurrent(values)) return; setEditor({ saved: { ...values }, draft: shippingSettingsDraft(values), storeVersion: JSON.stringify(store) }); setFailure(null); setSaved(false);
      }}>{t("edit")}</Button>}
      {editor && <form aria-label={t("edit")} aria-busy={pending || undefined} noValidate onSubmit={event => void save(event)} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); close(); } }}>
        {changed && <p role="status">{t("changed")}</p>}
        {issues.length > 0 && <div ref={summary} tabIndex={-1} role="alert"><p>{t("checkFields")}</p><ul>{issues.map(field => <li key={field}><button type="button" className="link-button" onClick={() => document.getElementById("shipping-settings-" + field)?.focus()}>{t(field)}: {t(validation[field])}</button></li>)}</ul></div>}
        <fieldset className="shipping-settings-fields" disabled={disabled}><legend>{t("edit")}</legend>
          {shippingSettingFields.map((field, index) => <Field key={field} ref={index === 0 ? firstField : undefined} id={"shipping-settings-" + field} name={field} label={t(field)} hint={t(hints[field])} error={fieldMessage(field)} inputMode="numeric" required
            value={editor.draft[field]} onChange={event => { setEditor({ ...editor, draft: { ...editor.draft, [field]: event.target.value } }); setIssues(current => current.filter(issue => issue !== field)); if (!failure?.uncertain) setFailure(null); }} />)}
        </fieldset><div className="cluster"><Button type="submit" disabled={disabled} busy={pending} busyLabel={t("saving")}>{t("save")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancel")}</Button></div>
      </form>}
      {failure && <div className="shipping-settings-outcome" ref={outcome} tabIndex={-1}><p role="status">{t(failure.uncertain ? "uncertain" : "refused")}</p><RequestError error={failure.error} operation="write"
        fieldErrors={editor ? shippingSettingFields.filter(field => fieldMessage(field)).map(field => ({ field: "shipping-settings-" + field, message: fieldMessage(field)! })) : []} /></div>}
      {failure?.uncertain && <Button type="button" variant="secondary" disabled={!fresh || !storeCurrent || pending} onClick={() => {
        if (!read.isCurrent(values) || !storeCurrent || pending) return; setEditor(null); setIssues([]); setFailure(null); heading.current?.focus();
      }}>{t("finishReview")}</Button>}
    </section>}
  </div>;
}
