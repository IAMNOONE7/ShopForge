import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { catalogImportApi, importCountFields, importFileProblem, type ImportReport } from "../api/catalogImport";
import { isHttpError, saveDownload } from "../api/http";
import { Button } from "../components/ui/Button";
import { RequestError } from "../components/ui/RequestError";
import { PlanUsageHint } from "../components/PlanLimitNotice";
import { canManageCatalog, useSession } from "../session";
import { ForbiddenPage } from "./RouteStatePage";
import "./StoreImportPage.css";

export function StoreImportPage() {
  const { user } = useSession(), { store } = useSelectedStore();
  if (!canManageCatalog(user.role)) return <ForbiddenPage />;
  return <ImportWorkspace key={[user.tenantId, user.id, user.role, store.id].join(":")} />;
}
type UploadOutcome = { kind: "report"; report: ImportReport; filename: string } | { kind: "refused" | "uncertain"; error: unknown };
function ImportWorkspace() {
  const { t, i18n } = useTranslation(["import", "navigation"]);
  const { store, storeRefreshing, storeRefreshError } = useSelectedStore();
  const [selection, setSelection] = useState<{ file: File; storeVersion: string } | null>(null);
  const [fileError, setFileError] = useState<NonNullable<ReturnType<typeof importFileProblem>> | "fileRequired" | null>(null);
  const [outcome, setOutcome] = useState<UploadOutcome | null>(null);
  const [downloadError, setDownloadError] = useState<unknown | null>(null);
  const [busy, setBusy] = useState<"template" | "import" | null>(null);
  const active = useRef(false), lock = useRef(false), controller = useRef<AbortController | null>(null);
  const chooser = useRef<HTMLButtonElement>(null), fileInput = useRef<HTMLInputElement>(null), feedback = useRef<HTMLDivElement>(null), downloadFeedback = useRef<HTMLDivElement>(null);
  const storeStale = storeRefreshing || storeRefreshError !== null;
  const changed = selection !== null && selection.storeVersion !== JSON.stringify(store);
  const uncertain = outcome?.kind === "uncertain";
  const disabled = busy !== null || storeStale || uncertain;
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (outcome) feedback.current?.focus(); }, [outcome]);
  useEffect(() => { if (downloadError) downloadFeedback.current?.focus(); }, [downloadError]);
  async function downloadTemplate() {
    if (lock.current || disabled) return;
    lock.current = true; setBusy("template"); setDownloadError(null);
    const request = new AbortController(); controller.current = request;
    try {
      const download = await catalogImportApi.template(store.id, request.signal);
      if (active.current && !request.signal.aborted) saveDownload(download);
    } catch (error) {
      if (active.current && !request.signal.aborted) setDownloadError(error);
    } finally {
      lock.current = false; controller.current = null; if (active.current) setBusy(null);
    }
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current || disabled || changed || fileError !== null || outcome?.kind === "report") return;
    if (!selection) { setFileError("fileRequired"); chooser.current?.focus(); return; }
    const problem = importFileProblem(selection.file);
    if (problem) { setFileError(problem); chooser.current?.focus(); return; }
    lock.current = true; setBusy("import"); setOutcome(null); setFileError(null);
    const request = new AbortController(); controller.current = request;
    try {
      const report = await catalogImportApi.apply(store.id, selection.file, request.signal);
      if (active.current && !request.signal.aborted) setOutcome({ kind: "report", report, filename: selection.file.name });
    } catch (error) {
      if (active.current && !request.signal.aborted) {
        const refused = isHttpError(error) && error.kind === "http" && [400, 401, 403, 404, 409, 413, 415, 429].includes(error.status ?? 0);
        setOutcome({ kind: refused ? "refused" : "uncertain", error });
      }
    } finally {
      lock.current = false; controller.current = null; if (active.current) setBusy(null);
    }
  }
  const fileDiagnostics = outcome?.kind === "refused" && isHttpError(outcome.error) ? Object.entries(outcome.error.problem?.errors ?? {}).filter(([key]) => key.toLowerCase() === "file").flatMap(([, value]) => Array.isArray(value) ? value.filter((message): message is string => typeof message === "string") : []) : [];
  const fileIssue = outcome?.kind === "refused" && isHttpError(outcome.error) && Object.keys(outcome.error.problem?.errors ?? {}).some(field => field.toLowerCase() === "file");
  return <div className="import-page">
    <Link to={"/stores/" + store.id}>{t("back")}</Link>
    <h1>{t("title")}</h1><p>{t("scope", { store: store.name })}</p>
    <section className="import-panel" aria-labelledby="import-prepare">
      <h2 id="import-prepare">{t("prepare")}</h2><p>{t("hint")}</p><p>{t("limits")}</p>
      <Button type="button" variant="secondary" disabled={disabled} busy={busy === "template"} busyLabel={t("downloading")} onClick={() => void downloadTemplate()}>{t("downloadTemplate")}</Button>
      {downloadError !== null && <div ref={downloadFeedback} tabIndex={-1}><RequestError error={downloadError} operation="download" /></div>}
      <details><summary>{t("formatGuide")}</summary><ul className="import-guide">
        <li>{t("identityGuide")}</li><li>{t("optionsGuide")}</li><li>{t("measurementsGuide")}</li><li>{t("taxGuide")}</li><li>{t("valuesGuide")}</li><li>{t("unsupportedGuide")}</li>
      </ul></details>
    </section>
    <section className="import-panel" aria-labelledby="import-upload">
      <h2 id="import-upload">{t("upload")}</h2><p>{t("writeHint")}</p>
      <PlanUsageHint resource="import" />
      {storeStale && <p role="status">{t("storeStale")}</p>}
      {changed && <p role="status">{t("storeChanged")}</p>}
      <form onSubmit={event => void submit(event)} noValidate aria-busy={busy === "import" || undefined}>
        <div className="import-file-field">
          <input hidden ref={fileInput} id="import-file" name="file" type="file" accept=".xlsx" aria-label={t("chooseFile")} disabled={disabled} onChange={event => {
            const file = event.target.files?.[0]; event.target.value = "";
            if (lock.current || disabled || !file) return;
            const problem = importFileProblem(file); setFileError(problem);
            if (problem) return;
            setSelection({ file, storeVersion: JSON.stringify(store) }); setOutcome(null);
          }} />
          <Button ref={chooser} id="import-file-picker" type="button" variant="secondary" disabled={disabled} aria-describedby={fileError || fileIssue ? "import-file-hint import-file-error" : "import-file-hint"} aria-invalid={fileError || fileIssue ? true : undefined} onClick={() => fileInput.current?.click()}>{t("chooseFile")}</Button>
          <p id="import-file-hint" className="hint">{t("selectionHint")}</p>
          {(fileError || fileIssue) && <p id="import-file-error" className="import-file-error">{fileError ? t(fileError) : t("fileRefused")}</p>}
        </div>
        {selection && <div className="import-selection"><strong>{t("selected")}</strong><span>{selection.file.name}</span><span>{t("fileBytes", { bytes: new Intl.NumberFormat(i18n.resolvedLanguage).format(selection.file.size) })}</span>
          <Button type="button" variant="secondary" disabled={disabled} onClick={() => { if (lock.current) return; setSelection(null); setFileError(null); if (outcome?.kind !== "report") setOutcome(null); chooser.current?.focus(); }}>{t("clear")}</Button>
        </div>}
        <Button type="submit" disabled={disabled || changed || fileError !== null || outcome?.kind === "report"} busy={busy === "import"} busyLabel={t("importing")}>{t("submit")}</Button>
      </form>
      {outcome && <div ref={feedback} className="import-feedback" tabIndex={-1}>
        {outcome.kind === "report" ? <ImportResults report={outcome.report} filename={outcome.filename} /> : <>
          <p role="status">{t(outcome.kind)}</p>
          <RequestError error={outcome.error} operation="write" fieldErrors={fileIssue ? [{ field: "import-file-picker", message: t("fileRefused") }] : []} />
          {fileDiagnostics.length > 0 && <details><summary>{t("diagnostic")}</summary>{fileDiagnostics.map((message, index) => <p key={index}>{message}</p>)}</details>}
        </>}
      </div>}
      <div className="cluster"><Link to={"/stores/" + store.id + "/products"}>{t("reviewListings")}</Link><Link to="/stock">{t("reviewStock")}</Link></div>
    </section>
  </div>;
}
function ImportResults({ report, filename }: { report: ImportReport; filename: string }) {
  const { t, i18n } = useTranslation("import");
  const [page, setPage] = useState(0), heading = useRef<HTMLHeadingElement>(null);
  const pageSize = 20, pages = Math.ceil(report.issues.length / pageSize);
  function changePage(next: number) { setPage(next); heading.current?.focus(); }
  return <section className="import-results" aria-labelledby="import-report-title">
    <h3 id="import-report-title" ref={heading} tabIndex={-1}>{t("reportTitle")}</h3><p role="status">{t("reportFor", { filename })}</p>
    <dl className="import-counts">{importCountFields.map(field => <div key={field}><dt>{t(`counts.${field}`)}</dt><dd>{new Intl.NumberFormat(i18n.resolvedLanguage).format(report[field])}</dd></div>)}</dl>
    <p>{t("reportHint")}</p>
    {report.ignoredColumns.length > 0 && <div><h4>{t("ignoredColumns")}</h4><ul>{report.ignoredColumns.map((column, index) => <li key={index}><code>{column}</code></li>)}</ul></div>}
    <h4>{t("diagnostics")}</h4><p>{t("diagnosticLimit")}</p>
    {report.issues.length === 0 ? <p>{t("noIssues")}</p> : <>
      <ul className="import-issues">{report.issues.slice(page * pageSize, (page + 1) * pageSize).map((issue, index) => <li key={page * pageSize + index}>
        <dl><div><dt>{t("row")}</dt><dd>{issue.row === 0 ? t("wholeWorkbook") : new Intl.NumberFormat(i18n.resolvedLanguage).format(issue.row)}</dd></div><div><dt>{t("column")}</dt><dd>{issue.column ?? t("general")}</dd></div></dl>
        <details><summary>{t("diagnostic")}</summary><p>{issue.message}</p></details>
      </li>)}</ul>
      {pages > 1 && <nav aria-label={t("issuePages")} className="cluster"><Button type="button" variant="secondary" disabled={page === 0} onClick={() => changePage(page - 1)}>{t("previous")}</Button><span>{t("page", { current: page + 1, total: pages })}</span><Button type="button" variant="secondary" disabled={page + 1 === pages} onClick={() => changePage(page + 1)}>{t("next")}</Button></nav>}
    </>}
    <p>{t("chooseAgain")}</p>
  </section>;
}
