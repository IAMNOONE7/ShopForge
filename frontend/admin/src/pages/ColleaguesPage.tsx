import { useEffect, useEffectEvent, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { colleaguesApi, colleagueRoles, canChangeColleague, normalizedEmail, type Colleague, type ColleagueRole, type Colleagues, type PendingInvitation } from "../api/colleagues";
import { isHttpError } from "../api/http";
import { statusOf } from "../api/errors";
import { Button } from "../components/ui/Button";
import { Field } from "../components/ui/Field";
import { LoadingState } from "../components/ui/LoadingState";
import { RequestError } from "../components/ui/RequestError";
import { canManageStore, knownRole, useSession } from "../session";
import { ForbiddenPage } from "./RouteStatePage";
import "./ColleaguesPage.css";

type Editor = { kind: "invite"; email: string; role: string; fixed: boolean; confirmed: boolean } | { kind: "role"; before: Colleague; role: string } | { kind: "activity"; before: Colleague } | { kind: "withdraw"; before: PendingInvitation };
type View = { kind: "loading" } | { kind: "ready"; data: Colleagues } | { kind: "failed" | "uncertain"; error: unknown } | { kind: "session-changed" };
type Notice = "refreshed" | "reviewed" | "changed" | "invited" | "withdrawn" | "roleChanged" | "activated" | "deactivated";
class SessionChanged extends Error {}
const identical = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

export function ColleaguesPage() {
  const { user } = useSession();
  if (!canManageStore(user.role)) return <ForbiddenPage />;
  return <ColleaguesWorkspace key={JSON.stringify([user.tenantId, user.id, user.email, user.role])} />;
}
function ColleaguesWorkspace() {
  const { user } = useSession(), { t, i18n } = useTranslation(["colleagues", "auth"]);
  const [view, setView] = useState<View>({ kind: "loading" }), [editor, setEditor] = useState<Editor | null>(null), [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<{ error: unknown; submitted: boolean } | null>(null), [notice, setNotice] = useState<{ kind: Notice; email?: string } | null>(null), [invalid, setInvalid] = useState<string[]>([]);
  const [userPage, setUserPage] = useState(0), [invitationPage, setInvitationPage] = useState(0);
  const active = useRef(false), controller = useRef<AbortController | null>(null), feedback = useRef<HTMLDivElement>(null), heading = useRef<HTMLHeadingElement>(null), panel = useRef<HTMLDivElement>(null), opener = useRef<HTMLButtonElement | null>(null);
  const owns = (request: AbortController) => active.current && controller.current === request && !request.signal.aborted;
  const labelRole = (role: string) => knownRole(role) === "unknown" ? role : t(`auth:roles.${knownRole(role)}`);
  const roles = colleagueRoles.filter(role => user.role === "Owner" || role !== "Owner");
  const data = view.kind === "ready" ? view.data : null;
  const replacement = editor?.kind === "invite" ? data?.invitations.find(row => normalizedEmail(row.email) === normalizedEmail(editor.email)) : undefined;
  const safeUserPage = Math.min(userPage, Math.max(0, Math.ceil((data?.users.length ?? 0) / 20) - 1));
  const safeInvitationPage = Math.min(invitationPage, Math.max(0, Math.ceil((data?.invitations.length ?? 0) / 20) - 1));
  const memberExists = editor?.kind === "invite" && data?.users.some(row => normalizedEmail(row.email) === normalizedEmail(editor.email));
  function beginRequest() {
    if (controller.current) return null;
    const request = new AbortController(); controller.current = request; setPending(true); return request;
  }
  function finish(request: AbortController) { if (controller.current === request) { controller.current = null; if (active.current) setPending(false); } }
  async function checkSession(request: AbortController) {
    const current = await colleaguesApi.session(request.signal);
    if (!owns(request)) return false;
    if (current.id !== user.id || current.tenantId !== user.tenantId || current.role !== user.role || normalizedEmail(current.email) !== normalizedEmail(user.email)) throw new SessionChanged();
    return true;
  }
  async function snapshot(request: AbortController) {
    if (!await checkSession(request)) return null;
    const rows = await colleaguesApi.list(request.signal);
    if (!owns(request) || !await checkSession(request)) return null;
    const self = rows.users.find(row => row.id === user.id);
    if (!self || !self.isActive || self.role !== user.role || normalizedEmail(self.email) !== normalizedEmail(user.email)) throw new SessionChanged();
    return rows;
  }
  function handleDenied(error: unknown) {
    if (error instanceof SessionChanged) { setView({ kind: "session-changed" }); setEditor(null); return true; }
    if ([401, 403].includes(statusOf(error) ?? 0)) { setView({ kind: "failed", error }); setEditor(null); return true; }
    return false;
  }
  async function reload(mode: "refresh" | "review" = "refresh") {
    const request = beginRequest(); if (!request) return;
    setEditor(null); setView({ kind: "loading" }); setFailure(null); setNotice(null); setInvalid([]);
    try {
      const rows = await snapshot(request); if (!rows || !owns(request)) return;
      setView({ kind: "ready", data: rows }); setUserPage(0); setInvitationPage(0);
      setNotice({ kind: mode === "review" ? "reviewed" : "refreshed" });
    } catch (error) { if (owns(request) && !handleDenied(error)) setView({ kind: mode === "review" ? "uncertain" : "failed", error }); }
    finally { finish(request); }
  }
  const loadInitially = useEffectEvent((request: AbortController) => {
    void snapshot(request)
      .then(rows => { if (rows && owns(request)) setView({ kind: "ready", data: rows }); })
      .catch(error => { if (owns(request) && !handleDenied(error)) setView({ kind: "failed", error }); })
      .finally(() => { if (controller.current === request) controller.current = null; });
  });
  useEffect(() => {
    active.current = true; const request = new AbortController(); controller.current = request; void loadInitially(request);
    return () => { active.current = false; controller.current?.abort(); controller.current = null; };
  }, []);
  useEffect(() => { if (failure || notice || invalid.length || ["failed", "uncertain", "session-changed"].includes(view.kind)) feedback.current?.focus(); }, [failure, notice, invalid, view.kind]);
  useEffect(() => { if (editor) (panel.current?.querySelector<HTMLElement>("input:not(:disabled), select") ?? panel.current)?.focus(); }, [editor?.kind]); // eslint-disable-line react-hooks/exhaustive-deps
  function open(next: Editor, button: HTMLButtonElement) {
    if (controller.current || editor || !data) return;
    opener.current = button; setEditor(next); setFailure(null); setNotice(null); setInvalid([]);
  }
  function close() {
    if (controller.current) return;
    setEditor(null); setFailure(null); setInvalid([]);
    requestAnimationFrame(() => { if (active.current) { if (opener.current?.isConnected && !opener.current.disabled) opener.current.focus(); else heading.current?.focus(); } });
  }
  async function save(event: FormEvent) {
    event.preventDefault(); if (!editor || !data || controller.current) return;
    const chosenRole = editor.kind === "invite" || editor.kind === "role" ? roles.find(role => role === editor.role) : undefined;
    const issues = editor.kind === "invite" ? [((editor.email.trim().length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(editor.email.trim())) || memberExists) && "email", !chosenRole && "role", replacement && !editor.confirmed && "replace"].filter((item): item is string => typeof item === "string") : editor.kind === "role" && !chosenRole ? ["role"] : [];
    setInvalid(issues); if (issues.length) return;
    if (editor.kind === "role" && editor.role === editor.before.role || (editor.kind === "role" || editor.kind === "activity") && !canChangeColleague(user, editor.before)) return;
    const request = beginRequest(); if (!request) return;
    setFailure(null); setNotice(null); let submitted = false;
    try {
      const rows = await snapshot(request); if (!rows || !owns(request)) return;
      const stale = editor.kind === "invite" ? rows.users.some(row => normalizedEmail(row.email) === normalizedEmail(editor.email)) || !identical(replacement, rows.invitations.find(row => normalizedEmail(row.email) === normalizedEmail(editor.email)))
        : !identical(editor.before, editor.kind === "withdraw" ? rows.invitations.find(row => row.id === editor.before.id) : rows.users.find(row => row.id === editor.before.id));
      if (stale) { setView({ kind: "ready", data: rows }); setEditor(null); setNotice({ kind: "changed" }); return; }
      let saved = rows, kind: Notice, email: string; submitted = true;
      if (editor.kind === "invite") {
        const row = await colleaguesApi.invite(editor.email, chosenRole!, request.signal); email = row.email; kind = "invited";
        saved = { ...rows, invitations: [...rows.invitations.filter(item => normalizedEmail(item.email) !== normalizedEmail(row.email)), row] };
      } else if (editor.kind === "withdraw") {
        await colleaguesApi.withdraw(editor.before.id, request.signal); email = editor.before.email; kind = "withdrawn";
        saved = { ...rows, invitations: rows.invitations.filter(row => row.id !== editor.before.id) };
      } else {
        const row = editor.kind === "role" ? await colleaguesApi.role(editor.before, chosenRole!, request.signal) : await colleaguesApi.activity(editor.before, request.signal);
        email = row.email; kind = editor.kind === "role" ? "roleChanged" : row.isActive ? "activated" : "deactivated";
        saved = { ...rows, users: rows.users.map(item => item.id === row.id ? row : item) };
      }
      if (!owns(request) || !await checkSession(request)) return;
      setView({ kind: "ready", data: saved }); setEditor(null); setNotice({ kind, email });
    } catch (error) {
      if (!owns(request) || handleDenied(error)) return;
      const refused = isHttpError(error) && error.kind === "http" && [400, 404, 409, 429].includes(error.status ?? 0);
      if (submitted && !refused) { setView({ kind: "uncertain", error }); setEditor(null); }
      else setFailure({ error, submitted });
    } finally { finish(request); }
  }
  const writeFields = failure && isHttpError(failure.error) ? Object.keys(failure.error.problem?.errors ?? {}).map(field => ({ field: `colleague-${field}`, message: t(field === "email" ? "colleagues:emailInvalid" : "colleagues:roleRequired") })) : [];
  const pageRows = <T,>(rows: T[], page: number) => rows.slice(page * 20, page * 20 + 20);
  function pager(count: number, page: number, setPage: (page: number) => void, name: string) {
    return count > 20 && <nav className="colleague-actions" aria-label={name}><Button type="button" variant="secondary" disabled={pending || editor !== null || page === 0} onClick={() => { setPage(page - 1); heading.current?.focus(); }}>{t("colleagues:previous")}</Button><span>{t("colleagues:page", { page: page + 1, pages: Math.ceil(count / 20) })}</span><Button type="button" variant="secondary" disabled={pending || editor !== null || (page + 1) * 20 >= count} onClick={() => { setPage(page + 1); heading.current?.focus(); }}>{t("colleagues:next")}</Button></nav>;
  }
  return <div className="colleagues-page"><div className="colleagues-heading"><h1 ref={heading} tabIndex={-1}>{t("colleagues:title")}</h1><Button type="button" variant="secondary" disabled={pending || view.kind === "loading" || editor !== null || view.kind === "session-changed" || view.kind === "uncertain"} onClick={() => void reload()}>{t("colleagues:refresh")}</Button></div>
    <p>{t("colleagues:scope")}</p><p className="hint">{t("colleagues:policy")}</p>
    {view.kind === "loading" && <LoadingState label={t("colleagues:loading")} />}
    <div ref={feedback} className="colleague-feedback" tabIndex={-1}>
      {notice && <p role="status">{t(`colleagues:${notice.kind}`, { email: notice.email ?? "" })}</p>}
      {invalid.length > 0 && <div role="alert"><p>{t("colleagues:checkFields")}</p><ul>{invalid.map(field => <li key={field}><button type="button" className="link-button" onClick={() => document.getElementById(`colleague-${field}`)?.focus()}>{t(field === "email" ? memberExists ? "colleagues:emailInTeam" : "colleagues:emailInvalid" : field === "role" ? "colleagues:roleRequired" : "colleagues:replacementRequired")}</button></li>)}</ul></div>}
      {failure && <><RequestError error={failure.error} operation={failure.submitted ? "write" : "read"} fieldErrors={writeFields} />{!failure.submitted && <p>{t("colleagues:notSubmitted")}</p>}{statusOf(failure.error) === 409 && <p>{t("colleagues:conflict")}</p>}</>}
      {(view.kind === "failed" || view.kind === "uncertain") && <><RequestError error={view.error} operation={view.kind === "uncertain" ? "write" : "read"} />{view.kind === "uncertain" && <><p>{t("colleagues:uncertain")}</p><Button type="button" onClick={() => void reload("review")}>{t("colleagues:review")}</Button></>}</>}
      {view.kind === "session-changed" && <><p role="alert">{t("colleagues:sessionChanged")}</p><a href={`${import.meta.env.BASE_URL}users`}>{t("colleagues:reloadAdmin")}</a></>}
    </div>
    {data && <>
      <Button type="button" disabled={pending || editor !== null} onClick={event => open({ kind: "invite", email: "", role: "", fixed: false, confirmed: false }, event.currentTarget)}>{t("colleagues:invite")}</Button>
      {editor && <div ref={panel} tabIndex={-1} role="region" aria-labelledby="colleague-editor-title" className="colleague-panel colleague-editor" onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); close(); } }}>
        <h2 id="colleague-editor-title">{t(editor.kind === "invite" ? "colleagues:invite" : editor.kind === "role" ? "colleagues:roleTitle" : editor.kind === "withdraw" ? "colleagues:withdrawTitle" : editor.before.isActive ? "colleagues:deactivateTitle" : "colleagues:activateTitle")}</h2>
        <form aria-label={t("colleagues:editor")} noValidate onSubmit={event => void save(event)} aria-busy={pending || undefined}>
          {editor.kind === "invite" ? <><Field id="colleague-email" name="email" type="email" autoComplete="off" label={t("colleagues:email")} value={editor.email} maxLength={254} required disabled={pending || editor.fixed} onChange={event => { setEditor({ ...editor, email: event.target.value, confirmed: false }); setInvalid([]); }} />{memberExists && <p>{t("colleagues:emailInTeam")}</p>}</> : <p>{t("colleagues:target", { email: editor.before.email, role: labelRole(editor.before.role) })}</p>}
          {(editor.kind === "invite" || editor.kind === "role") && <label htmlFor="colleague-role">{t("colleagues:role")}<select id="colleague-role" name="role" value={editor.role} disabled={pending} required onChange={event => { setEditor({ ...editor, role: event.target.value }); setInvalid([]); }}><option value="">{t("colleagues:chooseRole")}</option>{roles.map(role => <option key={role} value={role}>{labelRole(role)}</option>)}</select></label>}
          {editor.kind === "invite" && <><p>{t("colleagues:inviteHelp")}</p>{replacement && <><p>{t("colleagues:replaceWarning", { email: replacement.email })}</p><label className="colleague-check"><input id="colleague-replace" type="checkbox" checked={editor.confirmed} disabled={pending} onChange={event => { setEditor({ ...editor, confirmed: event.target.checked }); setInvalid([]); }} />{t("colleagues:confirmReplacement")}</label></>}</>}
          {editor.kind === "withdraw" && <p>{t("colleagues:withdrawHelp")}</p>}
          {editor.kind === "activity" && <p>{t(editor.before.isActive ? "colleagues:deactivateHelp" : "colleagues:activateHelp")}</p>}
          <div className="colleague-actions"><Button type="submit" variant={editor.kind === "withdraw" || editor.kind === "activity" && editor.before.isActive ? "danger" : "primary"} busy={pending} busyLabel={t("colleagues:saving")} disabled={editor.kind === "role" && editor.role === editor.before.role}>{t(editor.kind === "invite" ? replacement ? "colleagues:replaceConfirm" : "colleagues:inviteConfirm" : "colleagues:confirm")}</Button><Button type="button" variant="secondary" disabled={pending} onClick={close}>{t("colleagues:cancel")}</Button></div>
        </form>
      </div>}
      <section aria-labelledby="colleague-users-title"><h2 id="colleague-users-title">{t("colleagues:users", { count: data.users.length })}</h2><div className="colleague-grid">{pageRows(data.users, safeUserPage).map(person => <section key={person.id} className="colleague-panel" aria-label={person.email}><h3><bdi>{person.email}</bdi></h3><p>{labelRole(person.role)} · {t(person.isActive ? "colleagues:active" : "colleagues:inactive")}</p>
        {person.id === user.id ? <p className="hint">{t("colleagues:self")}</p> : canChangeColleague(user, person) ? <div className="colleague-actions"><Button type="button" variant="secondary" disabled={pending || editor !== null} aria-label={t("colleagues:roleAction", { email: person.email })} onClick={event => open({ kind: "role", before: person, role: person.role }, event.currentTarget)}>{t("colleagues:changeRole")}</Button><Button type="button" variant={person.isActive ? "danger" : "secondary"} disabled={pending || editor !== null} aria-label={t(person.isActive ? "colleagues:deactivateAction" : "colleagues:activateAction", { email: person.email })} onClick={event => open({ kind: "activity", before: person }, event.currentTarget)}>{t(person.isActive ? "colleagues:deactivate" : "colleagues:activate")}</Button></div> : <p className="hint">{t(knownRole(person.role) === "unknown" ? "colleagues:unknownRole" : "colleagues:ownerOnly")}</p>}
      </section>)}</div>{pager(data.users.length, safeUserPage, setUserPage, t("colleagues:userPages"))}</section>
      <section aria-labelledby="colleague-invitations-title"><h2 id="colleague-invitations-title">{t("colleagues:invitations", { count: data.invitations.length })}</h2><p>{t("colleagues:pendingHelp")}</p>{data.invitations.length === 0 && <p>{t("colleagues:noInvitations")}</p>}<div className="colleague-grid">{pageRows(data.invitations, safeInvitationPage).map(row => <section key={row.id} className="colleague-panel" aria-label={row.email}><h3><bdi>{row.email}</bdi></h3><p>{labelRole(row.role)}</p><p>{t("colleagues:expires")}: <time dateTime={row.expiresAt}>{new Intl.DateTimeFormat(i18n.resolvedLanguage, { dateStyle: "medium", timeStyle: "short" }).format(new Date(row.expiresAt))}</time></p>
        {knownRole(row.role) === "unknown" ? <p>{t("colleagues:unknownRole")}</p> : <div className="colleague-actions"><Button type="button" variant="secondary" disabled={pending || editor !== null} aria-label={t("colleagues:replaceAction", { email: row.email })} onClick={event => open({ kind: "invite", email: row.email, role: roles.includes(row.role as ColleagueRole) ? row.role : "", fixed: true, confirmed: false }, event.currentTarget)}>{t("colleagues:replace")}</Button><Button type="button" variant="danger" disabled={pending || editor !== null} aria-label={t("colleagues:withdrawAction", { email: row.email })} onClick={event => open({ kind: "withdraw", before: row }, event.currentTarget)}>{t("colleagues:withdraw")}</Button></div>}
      </section>)}</div>{pager(data.invitations.length, safeInvitationPage, setInvitationPage, t("colleagues:invitationPages"))}</section>
    </>}
  </div>;
}
