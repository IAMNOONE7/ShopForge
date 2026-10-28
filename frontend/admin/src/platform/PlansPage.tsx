import { useEffect, useEffectEvent, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { isHttpError } from "../api/http";
import { sameOperatorAccount } from "../api/operatorAccount";
import type { PlatformUser } from "../api/platform";
import { platformPlansApi, samePlan, planFields, type PlanField, type PlanValues, type PlatformPlan } from "../api/platformPlans";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { planDraft, reviewPlan, type PlanDraft } from "./planDraft";
import { operatorPrefix } from "./routing";
import "./PlansPage.css";

type View = { kind: "loading" } | { kind: "ready"; plans: PlatformPlan[] } | { kind: "failed"; error: unknown } | { kind: "closed"; reason: "changed" | "expired" | "forbidden" };
type Editor = { original: PlatformPlan | null; draft: PlanDraft; changed?: PlatformPlan | null };
type Outcome = { kind: "acknowledged"; saved: PlatformPlan; checked: boolean } | { kind: "uncertain"; checked: boolean };
type Failure = "refused" | "duplicate" | "limited" | "preflightFailed" | "noChanges";
class SessionChanged extends Error {}
const status = (error: unknown) => isHttpError(error) && error.kind === "http" ? error.status : null;
const find = (plans: PlatformPlan[], id: string) => plans.find(plan => plan.id.toLowerCase() === id.toLowerCase());

export function PlansPage({ user, sessionStale }: { user: PlatformUser; sessionStale: boolean }) {
  const { t } = useTranslation("operator");
  return sessionStale ? <p role="status">{t("sessionStale")}</p> : <PlansWorkspace key={JSON.stringify([user.id.toLowerCase(), user.email.trim().toLowerCase(), user.isTwoFactorEnabled])} user={user} />;
}
function PlansWorkspace({ user }: { user: PlatformUser }) {
  const { t } = useTranslation("platformPlans");
  const [view, setView] = useState<View>({ kind: "loading" }), [editor, setEditor] = useState<Editor | null>(null);
  const [review, setReview] = useState<PlanValues | null>(null), [issues, setIssues] = useState<PlanField[]>([]);
  const [failure, setFailure] = useState<Failure | null>(null), [outcome, setOutcome] = useState<Outcome | null>(null), [pending, setPending] = useState(false);
  const active = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null), reviewPanel = useRef<HTMLElement>(null), heading = useRef<HTMLHeadingElement>(null), firstField = useRef<HTMLInputElement>(null), newButton = useRef<HTMLButtonElement>(null), opener = useRef<HTMLButtonElement | null>(null);
  const owns = (request: AbortController) => active.current && controller.current === request && !request.signal.aborted;
  async function identity(request: AbortController) {
    const actual = await platformPlansApi.session(request.signal);
    if (!owns(request)) return false;
    if (!sameOperatorAccount(user, actual)) throw new SessionChanged();
    return true;
  }
  async function snapshot(request: AbortController) {
    if (!await identity(request)) return null;
    const plans = await platformPlansApi.list(request.signal);
    if (!owns(request) || !await identity(request)) return null;
    return plans;
  }
  function denied(error: unknown) {
    const code = status(error);
    if (!(error instanceof SessionChanged) && code !== 401 && code !== 403) return false;
    setView({ kind: "closed", reason: error instanceof SessionChanged ? "changed" : code === 401 ? "expired" : "forbidden" }); setEditor(null); setReview(null); setOutcome(null); setFailure(null); setIssues([]); return true;
  }
  function readFailure(error: unknown) { if (!denied(error)) setView({ kind: "failed", error }); }
  function finish(request: AbortController) { if (controller.current === request) { controller.current = null; if (active.current) setPending(false); } }
  async function load(request: AbortController) {
    try {
      const plans = await snapshot(request); if (!plans || !owns(request)) return;
      setView({ kind: "ready", plans });
      if (outcome) setOutcome({ ...outcome, checked: true });
      else if (editor?.original) {
        const current = find(plans, editor.original.id);
        if (!current || !samePlan(editor.original, current)) { setEditor({ ...editor, changed: current ?? null }); setReview(null); }
      }
    } catch (error) { if (owns(request)) readFailure(error); }
    finally { finish(request); }
  }
  const initialLoad = useEffectEvent((request: AbortController) => {
    void snapshot(request).then(plans => { if (plans && owns(request)) setView({ kind: "ready", plans }); })
      .catch(error => { if (owns(request)) readFailure(error); })
      .finally(() => { if (controller.current === request) controller.current = null; });
  });
  useEffect(() => {
    active.current = true; const request = new AbortController(); controller.current = request; initialLoad(request);
    return () => { active.current = false; controller.current?.abort(); controller.current = null; };
  }, []);
  useEffect(() => { if (failure || issues.length || outcome || view.kind === "failed" || view.kind === "closed" || editor?.changed !== undefined) feedback.current?.focus(); }, [failure, issues, outcome, view.kind, editor?.changed]);
  const reviewing = review !== null, focusEditor = editor !== null && outcome === null;
  useEffect(() => { if (reviewing) reviewPanel.current?.focus(); else if (focusEditor) firstField.current?.focus(); }, [focusEditor, reviewing]);
  function begin() {
    if (controller.current) return null;
    const request = new AbortController(); controller.current = request; setPending(true); return request;
  }
  function refresh() { if (view.kind === "closed") return; const request = begin(); if (!request) return; setView({ kind: "loading" }); void load(request); }
  function open(original: PlatformPlan | null, button: HTMLButtonElement) {
    if (view.kind !== "ready" || controller.current || editor) return;
    opener.current = button; setEditor({ original, draft: planDraft(original) }); setReview(null); setIssues([]); setFailure(null); setOutcome(null);
  }
  function leave() {
    if (controller.current || outcome && !outcome.checked || view.kind !== "ready") return;
    setEditor(null); setReview(null); setOutcome(null); setIssues([]); setFailure(null);
    requestAnimationFrame(() => { if (!active.current) return; if (opener.current?.isConnected) opener.current.focus(); else if (newButton.current) newButton.current.focus(); else heading.current?.focus(); });
  }
  function edit(field: PlanField, value: string) { if (!editor || controller.current || outcome) return; setEditor({ ...editor, draft: { ...editor.draft, [field]: value } }); setIssues([]); setFailure(null); }
  function propose(event: FormEvent) {
    event.preventDefault(); if (!editor || editor.changed !== undefined || controller.current || view.kind !== "ready" || outcome) return;
    const next = reviewPlan(editor.draft, editor.original); setIssues(next.issues); setFailure(null);
    if (!next.issues.length) {
      if (editor.original && samePlan(editor.original, { ...editor.original, ...next.values })) setFailure("noChanges");
      else setReview(next.values);
    }
  }
  async function confirm() {
    if (!editor || !review || editor.changed !== undefined || outcome || view.kind !== "ready") return;
    const request = begin(); if (!request) return;
    const values = review, original = editor.original; setFailure(null); setIssues([]);
    let sent = false;
    try {
      const before = await snapshot(request); if (!before || !owns(request)) return;
      setView({ kind: "ready", plans: before });
      if (original) {
        const current = find(before, original.id);
        if (!current || !samePlan(original, current)) { setEditor({ ...editor, changed: current ?? null }); setReview(null); return; }
      } else if (before.some(plan => plan.code === values.code)) { setFailure("duplicate"); setIssues(["code"]); setReview(null); return; }
      sent = true;
      const saved = original ? await platformPlansApi.update(original, values, request.signal) : await platformPlansApi.create(values, request.signal);
      if (!owns(request)) return;
      if (!original && before.some(plan => plan.id.toLowerCase() === saved.id.toLowerCase())) { setOutcome({ kind: "uncertain", checked: false }); setReview(null); return; }
      setOutcome({ kind: "acknowledged", saved, checked: false }); setReview(null);
      try {
        const plans = await snapshot(request); if (!plans || !owns(request)) return;
        setView({ kind: "ready", plans }); setOutcome({ kind: "acknowledged", saved, checked: true });
      } catch (error) { if (owns(request)) readFailure(error); }
    } catch (error) {
      if (!owns(request) || denied(error)) return;
      const code = status(error);
      if (!sent) { setFailure("preflightFailed"); setView({ kind: "failed", error }); }
      else if (code === 400 || code === 429 || code === 409 && !original) {
        setFailure(code === 429 ? "limited" : code === 409 ? "duplicate" : "refused"); setReview(null);
        setIssues(code === 409 ? ["code"] : isHttpError(error) ? planFields.filter(field => Object.hasOwn(error.problem?.errors ?? {}, field)) : []);
      } else if (code === 404 && original) { setEditor({ ...editor, changed: null }); setReview(null); setView({ kind: "ready", plans: view.plans.filter(plan => plan.id.toLowerCase() !== original.id.toLowerCase()) }); }
      else { setOutcome({ kind: "uncertain", checked: false }); setReview(null); }
    } finally { finish(request); }
  }
  const ready = view.kind === "ready", blocked = pending || !ready || Boolean(outcome), original = editor?.original ?? null;
  const currentSaved = outcome?.kind === "acknowledged" && ready ? find(view.plans, outcome.saved.id) : undefined;
  return <div className="platform-plans-page" onKeyDown={event => { if (event.key === "Escape" && !controller.current && editor && !outcome) { event.preventDefault(); if (review) setReview(null); else leave(); } }}>
    <div className="platform-plans-heading"><h1 ref={heading} tabIndex={-1}>{t("title")}</h1><Button type="button" variant="secondary" busy={pending || view.kind === "loading"} busyLabel={t("loading")} disabled={view.kind === "closed"} onClick={refresh}>{t("refresh")}</Button></div>
    <p>{t("intro")}</p>
    {view.kind === "loading" && <LoadingState label={t("loading")} />}
    <div className="platform-plans-feedback" ref={feedback} tabIndex={-1}>
      {view.kind === "closed" && <><p role="alert">{t(view.reason)}</p><a href={`${operatorPrefix()}/plans`}>{t("reload")}</a></>}
      {view.kind === "failed" && <RequestError error={view.error} operation="read" onRetry={refresh} />}
      {failure && <p role="alert">{t(failure)}</p>}
      {issues.length > 0 && <div role="alert"><p>{t("checkFields")}</p><ul>{issues.map(field => <li key={field}><button type="button" className="link-button" onClick={() => document.getElementById(`plan-${field}`)?.focus()}>{t(`invalid.${field}`)}</button></li>)}</ul></div>}
      {editor?.changed !== undefined && <div role="alert"><p>{t(editor.changed === null ? "missing" : "recordChanged")}</p><p>{t("draftKept")}</p></div>}
      {outcome && <>
        <p role={outcome.kind === "acknowledged" ? "status" : "alert"}>{t(outcome.kind === "uncertain" ? "uncertain" : outcome.checked ? "saved" : pending ? "checkingSaved" : "savedReadFailed")}</p>
        {outcome.kind === "uncertain" && outcome.checked && <p>{t("checkedUncertain")}</p>}
        {outcome.kind === "acknowledged" && outcome.checked && (!currentSaved || !samePlan(outcome.saved, currentSaved)) && <p role="alert">{t("changedAfterSave")}</p>}
        {!outcome.checked && <Button type="button" variant="secondary" busy={pending} busyLabel={t("loading")} onClick={refresh}>{t("checkCatalogue")}</Button>}
        <Button type="button" disabled={pending || !ready || !outcome.checked} onClick={leave}>{t("finishReview")}</Button>
      </>}
    </div>
    {ready && !editor && <>
      <Button ref={newButton} type="button" disabled={pending} onClick={event => open(null, event.currentTarget)}>{t("newPlan")}</Button>
      <p role="status">{t("count", { count: view.plans.length })}</p>
      {!view.plans.length ? <p>{t("empty")}</p> : <>
        {!view.plans.some(plan => plan.isDefault) && <p>{t("noDefault")}</p>}
        <ul className="platform-plans-list">{view.plans.map(plan => <li key={plan.id}><h2>{plan.name}</h2><PlanFacts plan={plan} /><Button type="button" variant="secondary" disabled={pending} onClick={event => open(plan, event.currentTarget)} aria-label={t("editNamed", { name: plan.name, code: plan.code })}>{t("edit")}</Button></li>)}</ul>
      </>}
    </>}
    {editor && view.kind !== "closed" && <section className="platform-plan-editor" aria-label={t(original ? "editPlan" : "newPlan")}>
      <h2>{t(original ? "editPlan" : "newPlan")}</h2>
      {original && <><h3>{t("previousTerms")}</h3><PlanFacts plan={original} /><p>{t("sharedImpact")}</p>{original.isDefault && <p>{t("defaultImpact")}</p>}</>}
      {review ? <section ref={reviewPanel} tabIndex={-1} className="platform-plan-review" aria-label={t("reviewTitle")}><h3>{t("reviewTitle")}</h3><p>{t(original ? "reviewEdit" : "reviewCreate")}</p><PlanFacts plan={{ ...review, isDefault: original?.isDefault ?? false }} /><div className="platform-plan-actions"><Button type="button" busy={pending} busyLabel={t("saving")} disabled={!ready} onClick={() => void confirm()}>{t(original ? "confirmEdit" : "confirmCreate")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={() => setReview(null)}>{t("backToForm")}</Button></div></section>
        : <form aria-label={t("form")} noValidate aria-busy={pending} onSubmit={propose}>
          {!original && <Field ref={firstField} id="plan-code" name="code" label={t("code")} required hint={t("codeHint")} value={editor.draft.code} disabled={blocked} error={issues.includes("code") ? t("invalid.code") : undefined} onChange={event => edit("code", event.target.value)} />}
          <Field ref={original ? firstField : undefined} id="plan-name" name="name" label={t("name")} maxLength={100} required value={editor.draft.name} readOnly={editor.changed !== undefined} disabled={blocked} error={issues.includes("name") ? t("invalid.name") : undefined} onChange={event => edit("name", event.target.value)} />
          <p className="hint">{t("capHint")}</p>
          {(["maxStores", "maxProducts"] as const).map(field => <Field key={field} id={`plan-${field}`} name={field} label={t(field)} inputMode="numeric" value={editor.draft[field]} readOnly={editor.changed !== undefined} disabled={blocked} error={issues.includes(field) ? t(`invalid.${field}`) : undefined} onChange={event => edit(field, event.target.value)} />)}
          <div className="platform-plan-actions"><Button type="submit" disabled={blocked || editor.changed !== undefined}>{t("review")}</Button>
            {!outcome && <Button type="button" variant="secondary" disabled={pending || !ready} onClick={leave}>{t("cancel")}</Button>}
          </div>
        </form>}
      {editor.changed && <section className="platform-plan-review"><h3>{t("latestTerms")}</h3><PlanFacts plan={editor.changed} /><Button type="button" variant="secondary" disabled={blocked} onClick={() => { const current = editor.changed!; setEditor({ original: current, draft: planDraft(current) }); setReview(null); setIssues([]); setFailure(null); requestAnimationFrame(() => { if (active.current) firstField.current?.focus(); }); }}>{t("reloadSaved")}</Button></section>}
      {outcome?.checked && currentSaved && <section className="platform-plan-review"><h3>{t("latestTerms")}</h3><PlanFacts plan={currentSaved} /></section>}
    </section>}
  </div>;
}
function PlanFacts({ plan }: { plan: Pick<PlatformPlan, "code" | "name" | "maxStores" | "maxProducts" | "isDefault"> }) {
  const { t, i18n } = useTranslation("platformPlans"), number = new Intl.NumberFormat(i18n.resolvedLanguage);
  return <dl className="platform-plan-facts"><div><dt>{t("name")}</dt><dd>{plan.name}</dd></div><div><dt>{t("code")}</dt><dd><code>{plan.code}</code></dd></div>{(["maxStores", "maxProducts"] as const).map(field => <div key={field}><dt>{t(field)}</dt><dd>{plan[field] === null ? t("uncapped") : number.format(plan[field])}</dd></div>)}<div><dt>{t("defaultLabel")}</dt><dd>{t(plan.isDefault ? "defaultYes" : "defaultNo")}</dd></div></dl>;
}
