'use client'

import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useClient } from '@servicestack/react'
import { Copy, MailPlus, RefreshCw, ShieldCheck, Trash2, UserRound, Users } from 'lucide-react'
import AppShell, { PageHeading, Panel, StatusPill } from '@/components/app-shell'
import { ValidateAuth, appAuth } from '@/lib/auth'
import { LoadingPanel, useSaasDashboard } from '@/lib/use-saas'
import { GetWorkspaceMembers, InviteWorkspaceMember, RemoveWorkspaceMember, ResendWorkspaceInvitation, UpdateWorkspaceMemberRole, WorkspaceKind, WorkspaceMemberInfo, WorkspaceMemberRole } from '@/lib/dtos'

function TeamPage() {
  const client = useClient()
  const { user } = appAuth()
  const { data, loading } = useSaasDashboard()
  const [members, setMembers] = useState<WorkspaceMemberInfo[]>([])
  const [showInvite, setShowInvite] = useState(false)
  const [notice, setNotice] = useState<string>()
  const [invitationUrl, setInvitationUrl] = useState<string>()
  const [busy, setBusy] = useState<string>()

  const load = useCallback(async () => {
    const api = await client.api(new GetWorkspaceMembers())
    if (api.succeeded) setMembers(api.response?.results ?? [])
  }, [client])
  useEffect(() => { void load() }, [load])

  const showInvitationResult = (member?: WorkspaceMemberInfo, resent = false) => {
    setInvitationUrl(member?.invitationUrl)
    setNotice(member?.invitationEmailSent
      ? resent ? 'A fresh invitation email was sent.' : 'Invitation email sent.'
      : 'Invitation created. Email delivery is in Development mode; copy the link below to test acceptance.')
  }
  const invite = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = event.currentTarget
    const values = new FormData(form)
    const api = await client.api(new InviteWorkspaceMember({ email: String(values.get('email')), role: String(values.get('role')) as WorkspaceMemberRole }))
    if (api.succeeded) { setShowInvite(false); form.reset(); showInvitationResult(api.response); await load() }
    else setNotice(api.error?.message)
  }
  const updateRole = async (member: WorkspaceMemberInfo, role: WorkspaceMemberRole) => {
    if (!member.id) return
    setBusy(member.id)
    const api = await client.api(new UpdateWorkspaceMemberRole({ id: member.id, role }))
    setNotice(api.succeeded ? 'Member role updated.' : api.error?.message)
    setBusy(undefined)
    if (api.succeeded) await load()
  }
  const resend = async (member: WorkspaceMemberInfo) => {
    if (!member.id) return
    setBusy(member.id)
    const api = await client.api(new ResendWorkspaceInvitation({ id: member.id }))
    if (api.succeeded) { showInvitationResult(api.response, true); await load() }
    else setNotice(api.error?.message)
    setBusy(undefined)
  }
  const remove = async (member: WorkspaceMemberInfo) => {
    if (!member.id) return
    setBusy(member.id)
    const api = await client.api(new RemoveWorkspaceMember({ id: member.id }))
    setNotice(api.succeeded ? member.status === 'Invited' ? 'Invitation revoked.' : 'Member access removed.' : api.error?.message)
    setBusy(undefined)
    if (api.succeeded) await load()
  }

  if (loading) return <AppShell><LoadingPanel /></AppShell>
  if (data?.workspace?.kind === WorkspaceKind.Individual) return <AppShell workspaceName={data.workspace.name}><PageHeading eyebrow="Account access" title="Team" description="Personal accounts are private and do not have team members. Create a business organization from Settings to collaborate." /></AppShell>
  const canAdminister = ['Owner', 'Admin'].includes(data?.memberRole ?? '')

  return <AppShell workspaceName={data?.workspace?.name}>
    <PageHeading eyebrow="Organization access" title="Team" description="Invite people to your organization and choose what they can manage. Invitations expire automatically and must be accepted by the invited email address." action={canAdminister && <button onClick={() => setShowInvite(!showInvite)} className="inline-flex items-center gap-2 rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white"><MailPlus className="h-4 w-4" />Invite member</button>} />
    {showInvite && <Panel className="mb-5 p-6"><form onSubmit={invite} className="grid items-end gap-4 sm:grid-cols-[1fr_180px_auto]"><label className="text-xs font-semibold">Email<input name="email" type="email" required placeholder="colleague@company.com" className="mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-white/5" /></label><label className="text-xs font-semibold">Role<select name="role" className="mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-[#0c1729]"><option>Member</option><option>Billing</option><option>Admin</option></select></label><button className="rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white">Create invitation</button></form></Panel>}
    {notice && <Panel className="mb-5 flex flex-col gap-3 border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 dark:border-blue-400/20 dark:bg-blue-400/10 dark:text-blue-100 sm:flex-row sm:items-center sm:justify-between"><span>{notice}</span>{invitationUrl && <button onClick={() => void navigator.clipboard.writeText(invitationUrl)} className="inline-flex shrink-0 items-center gap-2 self-start rounded-lg border border-blue-200 bg-white px-3 py-2 text-xs font-semibold text-blue-700 dark:border-blue-300/20 dark:bg-white/10 dark:text-blue-100"><Copy className="h-3.5 w-3.5" />Copy invitation link</button>}</Panel>}
    <Panel className="overflow-hidden">
      <div className="hidden grid-cols-[minmax(0,1fr)_auto_auto_auto] gap-4 border-b border-slate-200 bg-slate-50 px-6 py-3 text-[10px] font-bold uppercase tracking-[.12em] text-slate-400 dark:border-white/10 dark:bg-white/[.025] sm:grid"><span>Member</span><span>Role</span><span>Status</span><span>Actions</span></div>
      <div className="divide-y divide-slate-100 dark:divide-white/5">{members.filter(x => x.status !== 'Disabled').map(member => {
        const current = member.userId === user?.userId
        const canManage = canAdminister && member.role !== 'Owner' && !current
        const invited = member.status === 'Invited'
        return <div key={member.id} className="grid gap-4 px-6 py-5 sm:grid-cols-[minmax(0,1fr)_auto_auto_auto] sm:items-center">
          <div className="flex min-w-0 items-center gap-3">{current && user?.profileUrl ? <img className="h-10 w-10 rounded-[10px]" src={user.profileUrl} alt="" /> : <span className="grid h-10 w-10 shrink-0 place-items-center rounded-[10px] bg-slate-100 text-slate-400 dark:bg-white/5">{invited ? <MailPlus className="h-5 w-5" /> : <UserRound className="h-5 w-5" />}</span>}<div className="min-w-0"><p className="truncate text-sm font-semibold">{current ? user?.displayName || user?.userName : member.email || member.userId}</p><p className="mt-1 truncate text-xs text-slate-400">{current ? 'You' : invited ? member.invitationExpired ? 'Invitation expired' : `Expires ${member.invitationExpiresAt ? new Date(member.invitationExpiresAt).toLocaleDateString() : 'soon'}` : member.email || member.userId}</p></div></div>
          {canManage ? <select aria-label={`Role for ${member.email || member.userId}`} value={member.role} disabled={busy === member.id} onChange={event => void updateRole(member, event.target.value as WorkspaceMemberRole)} className="rounded-lg border-slate-200 bg-white py-1.5 text-xs dark:border-white/10 dark:bg-[#0c1729]"><option>Member</option><option>Billing</option><option>Admin</option></select> : <span className="text-sm font-medium">{member.role}</span>}
          <StatusPill tone={invited ? member.invitationExpired ? 'slate' : 'amber' : 'green'}>{member.invitationExpired ? 'Expired' : member.status}</StatusPill>
          <div className="flex justify-end gap-1">{invited && canManage && <button aria-label={`Resend invitation to ${member.email}`} title="Create a fresh invitation link" disabled={busy === member.id} onClick={() => void resend(member)} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 transition hover:bg-blue-50 hover:text-blue-600 disabled:opacity-25 dark:hover:bg-blue-400/10"><RefreshCw className="h-4 w-4" /></button>}<button aria-label={`${invited ? 'Revoke invitation for' : 'Remove'} ${member.email || member.userId}`} title={canManage ? invited ? 'Revoke invitation' : 'Remove access' : 'This member cannot be removed here'} disabled={!canManage || busy === member.id} onClick={() => void remove(member)} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 transition hover:bg-red-50 hover:text-red-600 disabled:opacity-25 dark:hover:bg-red-400/10"><Trash2 className="h-4 w-4" /></button></div>
        </div>
      })}</div>
    </Panel>
    <div className="mt-5 grid gap-5 md:grid-cols-2"><Panel className="p-6"><Users className="h-5 w-5 text-[#0b5cff]" /><h2 className="mt-4 font-semibold">Simple B2B roles</h2><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Owner, Admin, Billing, and Member cover the common operational split while remaining easy to extend.</p></Panel><Panel className="p-6"><ShieldCheck className="h-5 w-5 text-[#0b5cff]" /><h2 className="mt-4 font-semibold">Secure invitations</h2><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Links are single-use, expire automatically, and only work for the invited account.</p></Panel></div>
  </AppShell>
}

export default ValidateAuth(TeamPage)
