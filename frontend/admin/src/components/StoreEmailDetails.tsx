import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { emailStates, type EmailDnsRecord, type SendingDomain, type StoreEmail } from "../api/storeEmail";
import { formatDateTime } from "../utils/format";
import { Button } from "./ui/Button";

export function EmailOperationLink({ storeId, operationId }: { storeId: string; operationId: string }) {
  const { t } = useTranslation("storeEmail");
  return <Link to={`/stores/${storeId}/setup/${operationId}`}>{t("operationLink")} <code>{operationId}</code></Link>;
}
export function StoreEmailDetails({ item, storeId }: { item: StoreEmail; storeId: string }) {
  const { t, i18n } = useTranslation("storeEmail"), domain = item.domain;
  const state = emailStates.find(state => state === domain?.state);
  return <>
    <section className="email-panel" aria-labelledby="email-sender-title"><h2 id="email-sender-title">{t("currentSender")}</h2>
      <p>{t(item.sendsFromOwnDomain ? "ownSender" : "platformSender")}</p>
      <dl className="email-facts">
        <div><dt>{t("senderAddress")}</dt><dd>{(item.sendsFromOwnDomain ? domain?.senderAddress : item.platformAddress) || t("notProvided")}</dd></div>
        <div><dt>{t("platformAddress")}</dt><dd>{item.platformAddress || t("notProvided")}</dd></div>
      </dl><p className="hint">{t("deliveryHint")}</p>
    </section>
    {domain ? <section className="email-panel" aria-labelledby="email-domain-title">
      <h2 id="email-domain-title">{domain.domain}</h2><p className="email-state">{state ? t(`states.${state}`) : t("unknownState", { value: domain.state })}</p>
      <dl className="email-facts">
        <div><dt>{t("senderAddress")}</dt><dd>{domain.senderAddress}</dd></div>
        <div><dt>{t("replyTo")}</dt><dd>{domain.replyTo ?? t("notProvided")}</dd></div>
        <div><dt>{t("region")}</dt><dd>{domain.region === "eu" || domain.region === "us" ? t(`regions.${domain.region}`) : t("unknownRegion", { value: domain.region })}</dd></div>
        <div><dt>{t("verifiedAt")}</dt><dd>{domain.verifiedAt === null ? t("notProvided") : <time dateTime={domain.verifiedAt}>{formatDateTime(domain.verifiedAt, i18n.resolvedLanguage)}</time>}</dd></div>
      </dl>
      {domain.operationId ? <EmailOperationLink storeId={storeId} operationId={domain.operationId} /> : <p>{t("noOperation")}</p>}
      <p className="hint">{t("waitHint")}</p>
    </section> : <section className="email-panel"><h2>{t("noDomain")}</h2><p>{t("noDomainHint")}</p></section>}
    {domain && <>
      <section className="email-panel" aria-labelledby="email-provider-records"><h2 id="email-provider-records">{t("providerRecords")}</h2><p>{t("providerHint")}</p>
        <DnsRecords records={domain.records} domain={domain} group="provider" /></section>
      <section className="email-panel" aria-labelledby="email-recommended-records"><h2 id="email-recommended-records">{t("recommendedRecords")}</h2><p>{t("recommendedHint")}</p>
        <DnsRecords records={domain.recommended} domain={domain} group="recommended" /></section>
    </>}
  </>;
}
function DnsRecords({ records, domain, group }: { records: EmailDnsRecord[]; domain: SendingDomain; group: "provider" | "recommended" }) {
  const { t } = useTranslation("storeEmail");
  return records.length === 0 ? <p>{t("noRecords")}</p> : <ol className="email-records">{records.map((record, index) => {
    const purpose = ["sending", "receiving", "dmarc"].find(purpose => purpose === record.purpose) as "sending" | "receiving" | "dmarc" | undefined;
    return <li key={index} className="email-record"><h3>{t("recordHeading", { number: index + 1 })} · {purpose ? t(`purposes.${purpose}`) : t("unknownPurpose", { value: record.purpose })}</h3>
      <dl className="email-facts">{(["type", "name", "value", "priority", "valid"] as const).map(field => <div key={field}>
        <dt>{t(`dns.${field}`)}</dt><dd>
          {record[field] === null ? <>{t("notProvided")}{field === "name" && record.purpose === "receiving" && <>
            <p className="hint">{t("rootReceiving")}</p><DnsValue key={domain.domain} value={domain.domain} label={t("copyRoot", { number: index + 1 })} />
          </>}</> : <DnsValue key={record[field]} value={record[field]} label={t("copyField", { group: t(group === "provider" ? "providerShort" : "recommendedShort"), number: index + 1, field: t(`dns.${field}`) })} />}
        </dd></div>)}
      </dl>
    </li>;
  })}</ol>;
}
function DnsValue({ value, label }: { value: string; label: string }) {
  const { t } = useTranslation("storeEmail"), [copy, setCopy] = useState<"idle" | "copied" | "failed">("idle");
  async function write() { try { await navigator.clipboard.writeText(value); setCopy("copied"); } catch { setCopy("failed"); } }
  return <div className="email-dns-value"><code>{value === "" ? t("emptyValue") : value}</code>
    <Button type="button" variant="quiet" onClick={() => void write()} aria-label={label}>{t("copy")}</Button>
    {copy !== "idle" && <span role="status" className="hint">{t(copy === "copied" ? "copied" : "copyFailed")}</span>}
  </div>;
}
