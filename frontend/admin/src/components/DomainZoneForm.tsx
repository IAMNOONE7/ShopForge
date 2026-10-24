import { useEffect, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { StoreDomain } from "../api/domains";
import { isHttpError } from "../api/http";
import { domainZoneInput, type DomainZoneInput } from "./domainZoneValidation";
import { Button } from "./ui/Button";
import { Field } from "./ui/Field";

export function DomainZoneForm({ domain, storeName, disabled, changed, pending, error, onStart, onClose, onEdit }: {
  domain: StoreDomain; storeName: string; disabled: boolean; changed: boolean; pending: boolean; error: unknown;
  onStart: (input: DomainZoneInput) => Promise<void>; onClose: () => void; onEdit: () => void;
}) {
  const { t } = useTranslation("managedDns");
  const [draft, setDraft] = useState(""), [invalid, setInvalid] = useState(false);
  const [review, setReview] = useState<DomainZoneInput | null>(null), [acknowledged, setAcknowledged] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null), input = useRef<HTMLInputElement>(null), reviewButton = useRef<HTMLButtonElement>(null);
  const mounted = useRef(false);
  const submitting = useRef(false);
  useEffect(() => { mounted.current = true; input.current?.focus(); return () => { mounted.current = false; }; }, []);
  useEffect(() => { if (review) heading.current?.focus(); }, [review]);
  const defaultZone = domainZoneInput(domain.hostName, "")?.name;
  const serverFields = isHttpError(error) && error.kind === "http" && error.status === 400 ? error.problem?.errors : undefined;
  function prepare(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled || changed || review) return;
    const checked = domainZoneInput(domain.hostName, draft); setInvalid(checked === null);
    if (!checked) { input.current?.focus(); return; }
    onEdit(); setAcknowledged(false); setReview(checked);
  }
  async function start(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current || disabled || changed || !review || !acknowledged) return;
    submitting.current = true;
    try { await onStart(review); }
    finally {
      submitting.current = false;
      if (mounted.current) { setReview(null); setAcknowledged(false); input.current?.focus(); }
    }
  }
  return <section className="store-settings-panel domain-zone-editor" aria-labelledby="zone-editor-title" onKeyDown={event => {
    if (event.key === "Escape" && !pending && !submitting.current) {
      event.preventDefault();
      if (review) { setReview(null); setAcknowledged(false); window.requestAnimationFrame(() => reviewButton.current?.focus()); }
      else onClose();
    }
  }}>
    <h2 id="zone-editor-title">{t("title")}</h2>
    <p>{t("target", { host: domain.hostName, store: storeName })}</p>
    <p>{t("intro")}</p><p className="hint">{t("manual")}</p>
    <p id="zone-domain" tabIndex={-1}>{t("eligibility")}</p>
    {serverFields && Object.hasOwn(serverFields, "domainId") && <p role="status">{t("domainProved")}</p>}
    {changed && <p role="status">{t("changed")}</p>}
    <form noValidate aria-labelledby="zone-editor-title" onSubmit={prepare}>
      <Field ref={input} id="zone-name" name="zone" label={t("zoneLabel")} value={draft} disabled={disabled || changed || review !== null}
        hint={defaultZone ? t("defaultHint", { zone: defaultZone }) : t("explicitHint")}
        autoComplete="off" autoCapitalize="none" autoCorrect="off" spellCheck={false}
        error={invalid || serverFields && Object.hasOwn(serverFields, "zone") ? t("zoneInvalid") : undefined}
        onChange={event => { setDraft(event.target.value); setInvalid(false); onEdit(); }} />
      <p className="hint">{t("suffixHint")}</p>
      <div className="cluster"><Button ref={reviewButton} type="submit" disabled={disabled || changed || review !== null}>{t("review")}</Button>
        <Button type="button" variant="quiet" disabled={pending} onClick={onClose}>{t("close")}</Button></div>
    </form>
    {review && <form noValidate className="domain-confirmation domain-zone-review" aria-labelledby="zone-review-title" onSubmit={event => void start(event)}>
      <h3 id="zone-review-title" ref={heading} tabIndex={-1}>{t("reviewTitle")}</h3>
      <dl className="domain-edge-facts"><div><dt>{t("host")}</dt><dd><code>{domain.hostName}</code></dd></div>
        <div><dt>{t("zone")}</dt><dd><code>{review.name}</code></dd></div></dl>
      <p>{t("store", { store: storeName })}</p><p>{t(review.zone === null ? "defaultReview" : "explicitReview")}</p>
      <p>{t("warning")}</p><p>{t("noMigration")}</p>
      <label className="domain-zone-ack"><input type="checkbox" name="zoneAcknowledged" required checked={acknowledged} disabled={disabled || changed}
        onChange={event => setAcknowledged(event.target.checked)} /><span>{t("acknowledge")}</span></label>
      <div className="cluster"><Button type="submit" disabled={disabled || changed || !acknowledged} busy={pending} busyLabel={t("starting")}>{t("start")}</Button>
        <Button type="button" variant="secondary" disabled={pending} onClick={() => { setReview(null); setAcknowledged(false); window.requestAnimationFrame(() => reviewButton.current?.focus()); }}>{t("edit")}</Button></div>
    </form>}
  </section>;
}
