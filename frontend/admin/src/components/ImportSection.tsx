import { useState } from "react";
import { useTranslation } from "react-i18next";
import { api, type ImportReport } from "../api";
type Props = {
  storeId: string;
  run: (change: () => Promise<unknown>) => Promise<void>;
};
export function ImportSection({ storeId, run }: Props) {
  const { t } = useTranslation("import");
  const [report, setReport] = useState<ImportReport | null>(null);
  return (
    <section>
      <h2>{t("title")}</h2>
      <p className="hint">{t("hint")}</p>
      <div className="inline-form">
        <label className="upload">
          {t("chooseFile")}
          <input
            type="file"
            accept=".xlsx"
            onChange={(event) => {
              const file = event.target.files?.[0];
              event.target.value = "";
              if (file) {
                setReport(null);
                void run(async () =>
                  setReport(await api.importProducts(storeId, file)),
                );
              }
            }}
          />
        </label>
        <a href={`/api/admin/stores/${storeId}/import/template`}>
          {t("downloadTemplate")}
        </a>
      </div>
      {report && (
        <>
          <p className="import-summary">{t("summary", report)}</p>
          {report.ignoredColumns.length > 0 && (
            <p className="hint">
              {t("ignoredColumns", {
                columns: report.ignoredColumns.join(", "),
              })}
            </p>
          )}
          {report.issues.length > 0 && (
            <table>
              <thead>
                <tr>
                  <th>{t("row")}</th>
                  <th>{t("column")}</th>
                  <th>{t("problem")}</th>
                </tr>
              </thead>
              <tbody>
                {report.issues.map((issue, index) => (
                  <tr key={`${issue.row}-${issue.column}-${index}`}>
                    <td>{issue.row}</td>
                    <td>{issue.column ?? "—"}</td>
                    <td>
                      <details>
                        <summary>{t("diagnostic")}</summary>
                        {issue.message}
                      </details>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}
