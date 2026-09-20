import { useState } from 'react'
import { api, type ImportReport } from '../api'

type ImportSectionProps = {
  storeId: string
  run: (change: () => Promise<unknown>) => Promise<void>
}

export function ImportSection({ storeId, run }: ImportSectionProps) {
  const [report, setReport] = useState<ImportReport | null>(null)

  return (
    <section>
      <h2>Import</h2>
      <p className="hint">
        Upload an .xlsx file with a <code>sku</code> column. Rows update the matching product or create it, and only the columns in the file
        are changed.
      </p>
      <div className="inline-form">
        <label className="upload">
          Choose file
          <input
            type="file"
            accept=".xlsx"
            onChange={(event) => {
              const file = event.target.files?.[0]
              event.target.value = ''
              if (file) {
                setReport(null)
                void run(async () => setReport(await api.importProducts(storeId, file)))
              }
            }}
          />
        </label>
        <a href={`/api/admin/stores/${storeId}/import/template`}>Download template</a>
      </div>

      {report && (
        <>
          <p className="import-summary">
            Created {report.created} · Updated {report.updated} · Unchanged {report.skipped} · Invalid {report.invalid} · Failed{' '}
            {report.failed}
          </p>
          {report.ignoredColumns.length > 0 && <p className="hint">Ignored columns: {report.ignoredColumns.join(', ')}</p>}
          {report.issues.length > 0 && (
            <table>
              <thead>
                <tr>
                  <th>Row</th>
                  <th>Column</th>
                  <th>Problem</th>
                </tr>
              </thead>
              <tbody>
                {report.issues.map((issue, index) => (
                  <tr key={`${issue.row}-${issue.column}-${index}`}>
                    <td>{issue.row}</td>
                    <td>{issue.column ?? '—'}</td>
                    <td>{issue.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  )
}
