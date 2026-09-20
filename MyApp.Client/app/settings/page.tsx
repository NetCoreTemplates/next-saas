'use client'

import { FormEvent, useState } from 'react'
import { useClient } from '@servicestack/react'
import { Building2, ExternalLink, Plus, UserRound } from 'lucide-react'
import AppShell, { PageHeading, Panel } from '@/components/app-shell'
import { ValidateAuth, appAuth } from '@/lib/auth'
import { LoadingPanel, useSaasDashboard } from '@/lib/use-saas'
import { CreateOrganization, UpdateWorkspaceProfile, WorkspaceKind } from '@/lib/dtos'
import WorkspaceLifecycle from '@/components/workspace-lifecycle'

const inputClass = 'mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-white/5'

function SettingsPage() {
  const client = useClient()
  const { user } = appAuth()
  const { data, loading, refresh } = useSaasDashboard()
  const [notice, setNotice] = useState<string>()
  const [createNotice, setCreateNotice] = useState<string>()
  const [creating, setCreating] = useState(false)
  const individual = data?.workspace?.kind === WorkspaceKind.Individual

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const api = await client.api(new UpdateWorkspaceProfile({
      name: String(form.get('name')), slug: String(form.get('slug')),
      billingEmail: String(form.get('billingEmail')),
    }))
    setNotice(api.succeeded ? `${individual ? 'Account' : 'Organization'} saved.` : api.error?.message)
    if (api.succeeded) refresh()
  }

  const createOrganization = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setCreating(true)
    setCreateNotice(undefined)
    const form = new FormData(event.currentTarget)
    const api = await client.api(new CreateOrganization({
      name: String(form.get('organizationName')),
      billingEmail: String(form.get('organizationBillingEmail') || '') || undefined,
    }))
    if (api.succeeded) { window.location.href = '/dashboard'; return }
    setCreateNotice(api.error?.message ?? 'Unable to create the organization.')
    setCreating(false)
  }

  if (loading) return <AppShell><LoadingPanel /></AppShell>

  return <AppShell workspaceName={data?.workspace?.name}>
    <PageHeading eyebrow="Account configuration" title="Settings" description={individual ? 'Manage your individual account and personal data.' : 'Manage your organization and personal account.'} />
    <div className="grid gap-5 xl:grid-cols-[1.25fr_.75fr]">
      <Panel className="p-6">
        <div className="flex items-center gap-3"><Building2 className="h-5 w-5 text-[#0b5cff]" /><div><h2 className="font-semibold">{individual ? 'Account' : 'Organization'}</h2><p className="mt-1 text-xs text-slate-400">{individual ? 'Private to you and used for billing' : 'Shared with members and used for billing'}</p></div></div>
        <form onSubmit={save} className="mt-7 grid gap-5 sm:grid-cols-2">
          <label className="text-xs font-semibold">{individual ? 'Account name' : 'Organization name'}<input name="name" required defaultValue={data?.workspace?.name} className={inputClass} /></label>
          <input type="hidden" name="slug" value={data?.workspace?.slug ?? ''} />
          <label className="text-xs font-semibold">Billing email<input name="billingEmail" type="email" defaultValue={data?.workspace?.billingEmail} className={inputClass} /></label>
          <div className="flex items-center gap-4 sm:col-span-2"><button className="rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white">Save {individual ? 'account' : 'organization'}</button>{notice && <p className="text-xs text-[#0b5cff]">{notice}</p>}</div>
        </form>
      </Panel>
      <Panel className="p-6"><UserRound className="h-5 w-5 text-[#0b5cff]" /><h2 className="mt-4 font-semibold">Personal identity</h2><p className="mt-2 text-sm text-slate-500 dark:text-slate-400">{user?.userName}</p><a href="/Identity/Account/Manage" className="mt-5 inline-flex items-center gap-2 text-sm font-semibold text-[#0b5cff]">Manage identity <ExternalLink className="h-4 w-4" /></a><p className="mt-8 text-xs text-slate-500">Free-plan usage periods reset on calendar month boundaries in UTC.</p></Panel>
    </div>
    <div id="create-organization"><Panel className="mt-5 p-6">
      <div className="flex items-start gap-3"><Plus className="h-5 w-5 text-emerald-700" /><div><h2 className="font-semibold">{individual ? 'Create a business organization' : 'Create another organization'}</h2><p className="mt-1 text-sm leading-6 text-slate-500 dark:text-slate-400">Create a separate business organization with its own members, plan, usage, files, and API keys. You will become its Owner.</p></div></div>
      <form onSubmit={createOrganization} className="mt-6 grid gap-5 sm:grid-cols-2">
        <label className="text-xs font-semibold">Organization name<input name="organizationName" minLength={2} maxLength={100} required placeholder="Example, Inc." className={inputClass} /></label>
        <label className="text-xs font-semibold">Billing email <span className="font-normal text-slate-400">(optional)</span><input name="organizationBillingEmail" type="email" placeholder={user?.userName} className={inputClass} /></label>
        <div className="flex flex-col gap-3 sm:col-span-2"><button disabled={creating} className="inline-flex w-fit items-center gap-2 rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-50"><Plus className="h-4 w-4" />{creating ? 'Creating…' : 'Create organization'}</button>{createNotice && <p className="text-xs text-red-600 dark:text-red-400">{createNotice}</p>}</div>
      </form>
    </Panel></div>
    <WorkspaceLifecycle workspaceName={data?.workspace?.name} role={data?.memberRole} />
  </AppShell>
}

export default ValidateAuth(SettingsPage)
