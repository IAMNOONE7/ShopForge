import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { feedsApi, type FeedProblem, type StoreFeed } from "../api/feeds";
import { Button } from "./ui/Button";
import { RequestError } from "./ui/RequestError";

export function FeedFacts({ feed }: { feed: StoreFeed }) {
  const { t, i18n } = useTranslation("feeds");
  const number = (value: number | null) => value === null ? "—" : new Intl.NumberFormat(i18n.resolvedLanguage).format(value);
  return <>
    <p className="feed-state">{t(feed.isEnabled ? "enabled" : "disabled")}</p>
    <p>{t("lastAttempt")}: {feed.lastRunAt ? <time dateTime={feed.lastRunAt}>{new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: "medium", timeStyle: "short" }).format(new Date(feed.lastRunAt))}</time> : t("neverRun")}</p>
    {feed.lastError !== null && <div className="feed-run-error" role="status"><strong>{t("lastFailed")}</strong><p className="feed-literal">{feed.lastError}</p><p>{t("keepsPrevious")}</p></div>}
    <h3>{t("lastOutput")}</h3><p className="hint">{t("outputHint")}</p>
    <dl className="feed-facts">{(["lastProducts", "lastSkipped", "lastBytes", "lastMilliseconds"] as const).map(field => <div key={field}><dt>{t(field)}</dt><dd>{number(feed[field])}</dd></div>)}</dl>
  </>;
}

export function FeedAddress({ url }: { url: string }) {
  const { t } = useTranslation("feeds");
  const [shown, setShown] = useState(false), [copy, setCopy] = useState<"done" | "failed" | null>(null);
  const active = useRef(false), field = useRef<HTMLTextAreaElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  async function copyAddress() {
    try { await navigator.clipboard.writeText(url); if (active.current) setCopy("done"); }
    catch { if (active.current) { setCopy("failed"); field.current?.focus(); field.current?.select(); } }
  }
  return <div className="feed-address"><p>{t("addressPrivate")}</p>
    <Button type="button" variant="secondary" aria-expanded={shown} aria-controls="feed-address-value" onClick={() => { setShown(value => !value); setCopy(null); }}>{t(shown ? "hideAddress" : "showAddress")}</Button>
    {shown && <div id="feed-address-value"><label htmlFor="feed-address-input">{t("address")}</label><textarea ref={field} id="feed-address-input" readOnly value={url} rows={4} spellCheck={false} />
      <Button type="button" variant="secondary" onClick={() => void copyAddress()}>{t("copyAddress")}</Button>
      {copy && <p role="status">{t(copy === "done" ? "copied" : "copyFailed")}</p>}
    </div>}
  </div>;
}

export function FeedChecks({ storeId, feed, blocked }: { storeId: string; feed: string; blocked: boolean }) {
  const { t } = useTranslation("feeds");
  const [result, setResult] = useState<FeedProblem[] | null>(null), [error, setError] = useState<unknown | null>(null), [pending, setPending] = useState(false), [page, setPage] = useState(0);
  const active = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; controller.current?.abort(); }; }, []);
  useEffect(() => { if (result !== null || error !== null) feedback.current?.focus(); }, [result, error, page]);
  async function check() {
    if (blocked || controller.current) return;
    const request = new AbortController(); controller.current = request; setPending(true); setResult(null); setError(null); setPage(0);
    try { const problems = await feedsApi.check(storeId, feed, request.signal); if (active.current && !request.signal.aborted) setResult(problems); }
    catch (failure) { if (active.current && !request.signal.aborted) setError(failure); }
    finally { if (active.current) { controller.current = null; setPending(false); } }
  }
  return <section className="feed-panel" aria-labelledby="feed-check-title"><h2 id="feed-check-title">{t("checks")}</h2><p>{t("checkHint")}</p>
    {feed !== "google" ? <p>{t("noCheck")}</p> : <>
      <p className="hint">{t("googleCheck")}</p><Button type="button" disabled={blocked} busy={pending} busyLabel={t("checking")} onClick={() => void check()}>{t("check")}</Button>
      {(result !== null || error !== null) && <div ref={feedback} tabIndex={-1} className="feed-feedback">
        {error !== null ? <><RequestError error={error} operation="read" /><p>{t("checkUnavailable")}</p></> : <>
          <p role="status">{t(result!.length ? "problemCount" : "noProblems", { count: result!.length })}</p>
          <ul className="feed-problems">{result!.slice(page * 20, (page + 1) * 20).map((item, index) => <li key={page * 20 + index}><strong>{item.name}</strong> · <code>{item.sku}</code><ul>{item.problems.map((problem, i) => <li key={i} className="feed-literal">{problem}</li>)}</ul></li>)}</ul>
          {result!.length > 20 && <div className="cluster"><Button type="button" variant="secondary" disabled={page === 0} onClick={() => setPage(value => value - 1)}>{t("previous")}</Button><span>{t("page", { page: page + 1, total: Math.ceil(result!.length / 20) })}</span><Button type="button" variant="secondary" disabled={(page + 1) * 20 >= result!.length} onClick={() => setPage(value => value + 1)}>{t("next")}</Button></div>}
        </>}
      </div>}
    </>}
  </section>;
}
