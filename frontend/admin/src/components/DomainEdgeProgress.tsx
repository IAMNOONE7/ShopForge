import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { edgeStates, zoneStates, type StoreDomain } from "../api/domains";
import { Button } from "./ui/Button";

export function DomainEdgeProgress({ domain, storeId }: { domain: StoreDomain; storeId: string }) {
  const { t } = useTranslation("domainEdge"), id = useId(), { edge, zone } = domain;
  const state = edgeStates.find(state => state === edge?.state), zoneState = zoneStates.find(state => state === zone?.state);
  return <div className="domain-provisioning">
    <section className="domain-edge-panel" aria-labelledby={`${id}-edge`}>
      <h4 id={`${id}-edge`}>{t("edgeTitle")}</h4>
      {edge === null ? <p>{t("noEdge")}</p> : <>
        <p className="domain-edge-state">{state ? t(`states.${state}`) : t("unknownState", { value: edge.state })}</p>
        <p className="hint">{t("readinessHint")}</p>
        <dl className="domain-edge-facts"><div><dt>{t("providerValidation")}</dt><dd>{edge.validation === null ? t("notSupplied") : <code>{edge.validation === "" ? t("emptyValue") : edge.validation}</code>}</dd></div>
          <div><dt>{t("recordManagement")}</dt><dd>{t(edge.recordsByPlatform ? "platformManaged" : "manualManaged")}</dd></div></dl>
        {edge.operationId ? <Link to={`/stores/${storeId}/setup/${edge.operationId}`}>{t("edgeOperation")} <code>{edge.operationId}</code></Link> : <p>{t("noEdgeOperation")}</p>}
        <p className="hint">{t(edge.recordsByPlatform ? "platformRecordsHint" : "manualRecordsHint")}</p>
        {edge.records.length === 0 ? <p>{t(edge.recordsByPlatform ? "noManualRecords" : "recordsNotSupplied")}</p> : <ol className="domain-edge-records">{edge.records.map((record, index) => {
          const purpose = (["validation", "traffic"] as const).find(purpose => purpose === record.purpose);
          return <li key={index}><h5>{t("recordHeading", { number: index + 1 })} · {purpose ? t(`purposes.${purpose}`) : t("unknownPurpose", { value: record.purpose })}</h5>
            <dl className="domain-edge-facts">{(["type", "name", "value"] as const).map(field => <div key={field}><dt>{t(`fields.${field}`)}</dt><dd>
              <RecordValue key={record[field]} value={record[field]} label={t("copyRecord", { host: domain.hostName, number: index + 1, field: t(`fields.${field}`) })} />
            </dd></div>)}</dl>
            {record.purpose === "traffic" && record.type === "CNAME" && <p className="hint">{t("apexHint")}</p>}
          </li>;
        })}</ol>}
      </>}
    </section>
    <section className="domain-edge-panel" aria-labelledby={`${id}-zone`}><h4 id={`${id}-zone`}>{t("zoneTitle")}</h4>
      {zone === null ? <p>{t("noZone")}</p> : <>
        <dl className="domain-edge-facts"><div><dt>{t("zoneName")}</dt><dd><code>{zone.name}</code></dd></div>
          <div><dt>{t("zoneState")}</dt><dd>{zoneState ? t(`zoneStates.${zoneState}`) : t("unknownState", { value: zone.state })}</dd></div></dl>
        <p>{t("delegationHint")}</p>
        {zone.nameServers.length === 0 ? <p>{t("noNameServers")}</p> : <ul className="domain-nameservers">{zone.nameServers.map((server, index) => <li key={index}>
          <RecordValue key={server} value={server} label={t("copyNameServer", { host: domain.hostName, number: index + 1 })} />
        </li>)}</ul>}
        {zone.operationId ? <Link to={`/stores/${storeId}/setup/${zone.operationId}`}>{t("zoneOperation")} <code>{zone.operationId}</code></Link> : <p>{t("noZoneOperation")}</p>}
        <p className="hint">{t("zoneReadinessHint")}</p>
      </>}
    </section>
  </div>;
}
function RecordValue({ value, label }: { value: string; label: string }) {
  const { t } = useTranslation("domainEdge"), [state, setState] = useState<"idle" | "copied" | "failed">("idle");
  async function copy() { try { await navigator.clipboard.writeText(value); setState("copied"); } catch { setState("failed"); } }
  return <div className="domain-edge-value"><code>{value === "" ? t("emptyValue") : value}</code>
    <Button type="button" variant="quiet" aria-label={label} onClick={() => void copy()}>{t("copy")}</Button>
    {state !== "idle" && <p role="status" className="hint">{t(state === "copied" ? "copied" : "copyFailed")}</p>}
  </div>;
}
