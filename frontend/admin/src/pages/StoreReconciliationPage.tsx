import { Fragment, useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useLocation, useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { statusOf } from "../api/errors";
import { isHttpError } from "../api/http";
import { differenceKinds, isReportRange, reconciliationApi, reportDate, reportQuery, type ReconciliationReport, type ReportDifference, type ReportRange } from "../api/reconciliation";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, useSession } from "../session";
import { useRequest } from "../useRequest";
import { uiLocale } from "../utils/format";
import { ForbiddenPage } from "./RouteStatePage";
import "./StoreReconciliationPage.css";

export function StoreReconciliationPage() {
  const { user } = useSession(), { store } = useSelectedStore();
  if (!canManageStore(user.role)) return <ForbiddenPage />;
  return <ReportWorkspace key={JSON.stringify([user.tenantId, user.id, user.role, store.id])} storeId={store.id} />;
}
function ReportWorkspace({ storeId }: { storeId: string }) {
  const { t, i18n } = useTranslation("reconciliation");
  const [parameters, setParameters] = useSearchParams(), query = reportQuery(parameters);
  const location = useLocation(), [interacted, setInteracted] = useState(false);
  const key = location.key + ":" + JSON.stringify(query.range);
  function apply(range: ReportRange | null) { setInteracted(true); setParameters(range ?? {}); }
  return <div className="reconciliation-page">
    <h1>{t("title")}</h1><p>{t("intro")}</p>
    {query.invalid ? <div role="alert"><p>{t("invalidRange")}</p><Button type="button" onClick={() => apply(null)}>{t("reset")}</Button></div>
      : <ReportRead key={key} storeId={storeId} range={query.range} onApply={apply} focusOnResult={interacted} locale={uiLocale(i18n.resolvedLanguage)} />}
  </div>;
}
function ReportRead({ storeId, range, onApply, locale, focusOnResult }: { storeId: string; range: ReportRange | null; onApply: (range: ReportRange | null) => void; locale: string; focusOnResult: boolean }) {
  const { t } = useTranslation("reconciliation"), { storeRefreshing, storeRefreshError } = useSelectedStore();
  const provider = useRef<string | undefined>(undefined);
  const [state, reload] = useRequest(`report:${storeId}:${JSON.stringify(range)}`, signal => reconciliationApi.read(storeId, range, signal, provider.current));
  const error = state.status === "ready" ? state.refreshError : state.status === "loading" ? null : state.error;
  const denied = [401, 403, 404].includes(statusOf(error) ?? 0);
  const report = state.status === "ready" && !denied ? state.data : null;
  const summary = useRef<HTMLHeadingElement>(null), feedback = useRef<HTMLDivElement>(null), requested = useRef(focusOnResult);
  useEffect(() => { if (report) provider.current = report.provider; }, [report]);
  useEffect(() => {
    if (requested.current && state.status !== "loading" && !(state.status === "ready" && state.refreshing)) { requested.current = false; (summary.current ?? feedback.current)?.focus(); }
  }, [state]);
  function refresh() { requested.current = true; reload(); }
  const refreshing = state.status === "loading" || state.status === "ready" && state.refreshing;
  const stale = state.status === "ready" && (state.refreshing || error !== null) || storeRefreshing || storeRefreshError !== null;
  const fieldErrors = isHttpError(error) ? Object.keys(error.problem?.errors ?? {}).flatMap(field => {
    const name = field.toLowerCase();
    return name === "from" || name === "to" ? [{ field: "reconciliation-" + name, message: `${t(name)}: ${t("invalidRange")}` }] : [];
  }) : [];
  return <>
    <div className="reconciliation-actions"><Button type="button" variant="secondary" onClick={refresh} disabled={refreshing}>{t("refresh")}</Button>
      <Button type="button" variant="secondary" onClick={() => onApply(null)} disabled={range === null}>{t("reset")}</Button></div>
    <RangeForm key={JSON.stringify(range)} initial={range ?? report} onApply={onApply} />
    {refreshing && <LoadingState label={t("loading")} lines={3} />}
    {error !== null && <div ref={feedback} tabIndex={-1}><RequestError error={error} operation="read" onRetry={refresh} fieldErrors={fieldErrors} /></div>}
    {denied && <p>{t(statusOf(error) === 404 ? "unavailable" : "denied")}</p>}
    {report && <section className="reconciliation-panel" aria-labelledby="reconciliation-source">
      <h2 id="reconciliation-source" ref={summary} tabIndex={-1}>{t("source")}</h2>
      {stale && <p role="status">{t("stale")}</p>}
      <dl className="reconciliation-facts">
        <div><dt>{t("provider")}</dt><dd>{report.provider}</dd></div><div><dt>{t("timeZone")}</dt><dd>{report.timeZone}</dd></div>
        <div><dt>{t("period")}</dt><dd>{reportDate(report.from, locale)} – {reportDate(report.to, locale)}</dd></div>
        <div><dt>{t("latestStatement")}</dt><dd>{report.statementsThrough === null ? t("unknown") : reportDate(report.statementsThrough, locale)}</dd></div>
        <div><dt>{t("testPayments")}</dt><dd>{new Intl.NumberFormat(locale).format(report.testPayments)}</dd></div>
        <div><dt>{t("unexplained")}</dt><dd>{new Intl.NumberFormat(locale).format(report.unexplained)}</dd></div>
      </dl><p className="hint">{t("coverage")}</p><p className="hint">{t("paymentScope")}</p>
      {report.differences.length === 0 && <p role="status">{t("empty")}</p>}
      <DifferenceGroup report={report} explained={false} storeId={storeId} locale={locale} />
      <DifferenceGroup report={report} explained storeId={storeId} locale={locale} />
      <DailyActivity days={report.days} locale={locale} onApply={onApply} />
    </section>}
  </>;
}
function DailyActivity({ days, locale, onApply }: {
  days: ReadonlyArray<Pick<ReconciliationReport["days"][number], "date" | "paid" | "unexplained">>;
  locale: string;
  onApply: (range: ReportRange) => void;
}) {
  const { t } = useTranslation("reconciliation"), number = new Intl.NumberFormat(locale);
  return <section className="reconciliation-daily" aria-labelledby="reconciliation-daily-title" aria-describedby="reconciliation-daily-hint">
    <h3 id="reconciliation-daily-title">{t("dailyTitle")}</h3>
    <p id="reconciliation-daily-hint" className="hint">{t("dailyHint")}</p>
    <ul>{days.map(day => <li key={day.date}>
      <h4><time dateTime={day.date}>{reportDate(day.date, locale)}</time></h4>
      <dl>
        <div><dt>{t("recordedPaid")}</dt><dd><ActivityCount value={day.paid} number={number} /></dd></div>
        <div><dt>{t("unexplained")}</dt><dd><ActivityCount value={day.unexplained} number={number} /></dd></div>
      </dl>
      <Button type="button" variant="secondary" aria-label={t("reviewDay", { date: reportDate(day.date, locale) })}
        onClick={() => onApply({ from: day.date, to: day.date })}>{t("viewDay")}</Button>
    </li>)}</ul>
  </section>;
}
function ActivityCount({ value, number }: { value: number; number: Intl.NumberFormat }) {
  const groups: string[] = [];
  let group = "";
  for (const part of number.formatToParts(value)) {
    group += part.value;
    if (part.type === "group") { groups.push(group); group = ""; }
  }
  if (group) groups.push(group);
  return <bdi>{groups.map((text, index) => <Fragment key={index}>
    {index > 0 && <wbr />}<span className="reconciliation-count-group">{text}</span>
  </Fragment>)}</bdi>;
}
function RangeForm({ initial, onApply }: { initial: ReportRange | null; onApply: (range: ReportRange) => void }) {
  const { t } = useTranslation("reconciliation");
  const [draft, setDraft] = useState<ReportRange | null>(null), [invalid, setInvalid] = useState(false);
  const values = draft ?? { from: initial?.from ?? "", to: initial?.to ?? "" };
  const notice = useRef<HTMLParagraphElement>(null);
  function submit(event: FormEvent) {
    event.preventDefault();
    if (!isReportRange(values)) { setInvalid(true); window.requestAnimationFrame(() => notice.current?.focus()); return; }
    setInvalid(false); onApply(values);
  }
  return <form className="reconciliation-range" aria-label={t("range")} onSubmit={submit} noValidate>
    <p className="hint">{t("rangeHint")}</p>
    <div className="reconciliation-range-fields">{(["from", "to"] as const).map(field => <Field key={field} id={"reconciliation-" + field} name={field} type="date" required label={t(field)} value={values[field]}
      aria-invalid={invalid || undefined} aria-describedby={invalid ? "reconciliation-range-error" : undefined} onChange={event => { setDraft({ ...values, [field]: event.target.value }); setInvalid(false); }} />)}
      <Button type="submit">{t("apply")}</Button></div>
    {invalid && <p id="reconciliation-range-error" ref={notice} tabIndex={-1} role="alert">{t("invalidRange")}</p>}
  </form>;
}
function DifferenceGroup({ report, explained, storeId, locale }: { report: ReconciliationReport; explained: boolean; storeId: string; locale: string }) {
  const { t } = useTranslation("reconciliation"), rows = report.differences.filter(item => item.explained === explained);
  const [page, setPage] = useState(0), heading = useRef<HTMLHeadingElement>(null);
  if (!rows.length) return null;
  const current = Math.min(page, Math.ceil(rows.length / 25) - 1), start = current * 25;
  const number = (value: number) => new Intl.NumberFormat(locale).format(value);
  function move(next: number) { setPage(next); window.requestAnimationFrame(() => heading.current?.focus()); }
  return <section className="reconciliation-differences" aria-labelledby={"reconciliation-" + explained}>
    <h3 id={"reconciliation-" + explained} ref={heading} tabIndex={-1}>{t(explained ? "explained" : "unexplained")}</h3>
    <ul>{rows.slice(start, start + 25).map((item, index) => <li key={start + index}><Difference item={item} storeId={storeId} locale={locale} /></li>)}</ul>
    {rows.length > 25 && <nav className="reconciliation-actions" aria-label={t(explained ? "explained" : "unexplained")}>
      <Button type="button" variant="secondary" disabled={current === 0} onClick={() => move(current - 1)}>{t("previous")}</Button>
      <p role="status">{t("showing", { from: number(start + 1), to: number(Math.min(start + 25, rows.length)), total: number(rows.length) })}</p>
      <Button type="button" variant="secondary" disabled={start + 25 >= rows.length} onClick={() => move(current + 1)}>{t("next")}</Button>
    </nav>}
  </section>;
}
function Difference({ item, storeId, locale }: { item: ReportDifference; storeId: string; locale: string }) {
  const { t } = useTranslation("reconciliation");
  const known = differenceKinds.find(kind => kind === item.kind);
  return <>
    <div className="reconciliation-entry-heading"><strong>{known ? t(`kinds.${known}`) : t("unknownKind")}</strong><time dateTime={item.date}>{reportDate(item.date, locale)}</time></div>
    {!known && <p>{item.kind}</p>}
    <p className="reconciliation-detail" lang={known && item.kind !== "unreadableLine" ? "en" : undefined}>{item.detail}</p>
    {item.orderNumber && <p><Link to={`/stores/${encodeURIComponent(storeId)}/orders/${encodeURIComponent(item.orderNumber)}`}>{t("order", { number: item.orderNumber })}</Link></p>}
    {item.transactionId && <p>{t("transaction")}: <code>{item.transactionId}</code></p>}
    {item.kind === "keptForRefund" && <Link to={`/stores/${encodeURIComponent(storeId)}/unapplied-payments`}>{t("unapplied")}</Link>}
  </>;
}
