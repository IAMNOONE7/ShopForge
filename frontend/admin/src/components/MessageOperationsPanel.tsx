import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { isHttpError } from "../api/http";
import { useAction } from "../useAction";
import { useRequest } from "../useRequest";
import { formatNumber } from "../utils/format";
import { Button } from "./ui/Button";
import { InlineMessage } from "./ui/InlineMessage";
import { LoadingState } from "./ui/LoadingState";
import { RequestError } from "./ui/RequestError";

type Kind = "messages" | "suppressed";

export function MessageOperationsPanel<T extends { id: string }>({
  kind, scope, canManage, storeFresh, load, write, label, renderRecord,
}: {
  kind: Kind; scope: string; canManage: boolean; storeFresh: boolean;
  load: (signal: AbortSignal) => Promise<T[]>;
  write: (row: T) => Promise<void>;
  label: (row: T) => string;
  renderRecord: (row: T) => ReactNode;
}) {
  const { t, i18n } = useTranslation("operations");
  const [state, reload] = useRequest(`${scope}:${kind}`, async signal => ({ items: await load(signal) }));
  const [confirmation, setConfirmation] = useState<T | null>(null);
  const [result, setResult] = useState<{ row: T; confirmed: boolean } | null>(null);
  const [invalidated, setInvalidated] = useState<{ items: T[] } | null>(null);
  const [error, run, pending] = useAction(() => undefined, { manageControls: false });
  const active = useRef(false), heading = useRef<HTMLHeadingElement>(null);
  const confirmHeading = useRef<HTMLHeadingElement>(null), opener = useRef<HTMLButtonElement | null>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => { if (confirmation) confirmHeading.current?.focus(); }, [confirmation]);

  const ready = state.status === "ready" ? state : null;
  const items = ready?.data.items ?? [];
  const refreshing = state.status === "loading" || ready?.refreshing === true;
  const needsRead = !ready || ready.refreshing || ready.refreshError !== null || ready.data === invalidated;
  const disabled = !canManage || !storeFresh || pending || needsRead;
  const changed = confirmation !== null && !items.some(row => JSON.stringify(row) === JSON.stringify(confirmation));
  const absent = result !== null && !result.confirmed && !needsRead && !items.some(row => row.id === result.row.id);
  const readError = ready?.refreshError ?? (state.status === "error" || state.status === "not-found" ? state.error : null);
  const uncertainty = !isHttpError(error) || error.kind !== "http" || error.status === null || error.status >= 500 || error.status === 408;

  function open(row: T, button: HTMLButtonElement) {
    if (disabled || confirmation || !items.includes(row)) return;
    opener.current = button; setResult(null); setConfirmation(row);
  }
  function close() {
    if (pending) return;
    setConfirmation(null);
    window.requestAnimationFrame(() => {
      if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus();
      else heading.current?.focus();
    });
  }
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (disabled || changed || !confirmation || !ready) return;
    const row = confirmation;
    setResult(null);
    await run(async () => {
      let confirmed = false;
      try { await write(row); confirmed = true; }
      finally {
        if (active.current) {
          setResult({ row, confirmed }); setConfirmation(null);
          // A failed answer may follow a committed action. Only refresh the read; never replay the write.
          setInvalidated(ready.data); reload(); heading.current?.focus();
        }
      }
    });
  }

  return <section className="message-operations-panel" aria-labelledby={`${kind}-title`}>
    <div className="section-heading">
      <h2 id={`${kind}-title`} ref={heading} tabIndex={-1}>{t(`${kind}.title`)}</h2>
      <Button type="button" variant="secondary" disabled={refreshing || pending} onClick={reload}>{t(`${kind}.refresh`)}</Button>
    </div>
    <p className="hint">{t(`${kind}.hint`)}</p>
    <p className="hint">{t(`${kind}.limit`)}</p>
    {refreshing && <LoadingState label={t(ready ? "refreshing" : "loading")} lines={2} />}
    {readError !== null && <RequestError error={readError} operation="read" onRetry={reload} />}
    {ready && needsRead && <InlineMessage>{t("staleRead")}</InlineMessage>}
    {result && <div className="operation-result">
      <p className="operation-target">{t("target", { target: label(result.row) })} <code>{result.row.id}</code></p>
      {result.confirmed ? <InlineMessage tone="success">{t(`${kind}.success`)}</InlineMessage> : <>
        {error !== null && <RequestError error={error} operation="write" fieldErrors={[]} />}
        <InlineMessage>{t(isHttpError(error) && error.status === 404 ? "missingTarget" : uncertainty ? "uncertain" : "refused")}</InlineMessage>
        {absent && <p className="hint">{t("absent")}</p>}
      </>}
    </div>}
    {ready && <p className="hint">{t("count", { count: items.length, formatted: formatNumber(items.length, i18n.resolvedLanguage) })}</p>}
    {ready && !needsRead && items.length === 0 && <InlineMessage>{t(`${kind}.empty`)}</InlineMessage>}
    {items.length > 0 && <ul className="operation-records">{items.map(row => <li key={row.id}>
      <h3>{label(row)}</h3>
      {renderRecord(row)}
      {canManage && <Button type="button" variant="secondary" disabled={disabled || confirmation !== null} onClick={event => open(row, event.currentTarget)}>{t(`${kind}.action`)}</Button>}
    </li>)}</ul>}
    {confirmation && <form className="operation-confirmation" aria-labelledby={`${kind}-confirm-title`} onSubmit={submit}
      onKeyDown={event => { if (event.key === "Escape" && !pending) { event.preventDefault(); close(); } }}>
      <h3 id={`${kind}-confirm-title`} ref={confirmHeading} tabIndex={-1}>{t(`${kind}.confirmTitle`)}</h3>
      <p className="operation-target">{label(confirmation)}</p>
      {renderRecord(confirmation)}
      <p>{t(`${kind}.confirmHint`)}</p>
      {changed && <InlineMessage>{t("changed")}</InlineMessage>}
      <div className="cluster">
        <Button type="submit" busy={pending} busyLabel={t("working")} disabled={disabled || changed}>{t(`${kind}.action`)}</Button>
        <Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("cancel")}</Button>
      </div>
    </form>}
  </section>;
}
