'use client'

import { useEffect, useRef, useState } from 'react'
import { Code2, KeyRound, LockKeyhole, TerminalSquare } from 'lucide-react'
import AppShell, { PageHeading, Panel, StatusPill } from '@/components/app-shell'
import { ValidateAuth } from '@/lib/auth'
import FeatureGate from '@/components/feature-gate'
import { GetWorkspaceApiKeys, WorkspaceApiKeyInfo } from '@/lib/dtos'
import { client } from '@/lib/gateway'
import { LoadingPanel, useSaasDashboard } from '@/lib/use-saas'

function formatDate(value?: string) {
  return value ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(value)) : 'Never'
}

function ApiKeysPage() {
  const { data, loading } = useSaasDashboard()
  const [keys, setKeys] = useState<WorkspaceApiKeyInfo[]>([])
  const [keysLoading, setKeysLoading] = useState(true)
  const [keysError, setKeysError] = useState<string>()
  const loaded = useRef(false)

  useEffect(() => {
    if (loaded.current) return
    loaded.current = true
    void client.api(new GetWorkspaceApiKeys()).then(api => {
      if (api.succeeded) setKeys(api.response?.results ?? [])
      else setKeysError(api.error?.message || 'Unable to load API keys.')
      setKeysLoading(false)
    })
  }, [])

  const curl = `curl -X POST /api/RecordUsage \\
  -H "X-Api-Key: $API_KEY" \\
  -H "Content-Type: application/json" \\
  -d '{
    "meterKey":"api.requests",
    "units":1,
    "idempotencyKey":"op_123"
  }'`

  if (loading) return <AppShell><LoadingPanel /></AppShell>

  const activeKeys = keys.filter(x => x.active).length
  return <AppShell workspaceName={data?.workspace?.name}>
    <PageHeading
      eyebrow="Developer access"
      title="API keys"
      description="Create revocable credentials for services and automations. Raw keys are shown once and never stored in reversible application fields."
      action={<a href="/Identity/Account/Manage/ApiKeys" className="inline-flex items-center gap-2 rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white"><KeyRound className="h-4 w-4" />Manage API keys</a>}
    />
    <FeatureGate feature="api.access" entitlements={data?.entitlements} title="API access is not included in this plan">
      <div className="grid gap-5 xl:grid-cols-[1.2fr_.8fr]">
        <Panel className="overflow-hidden">
          <div className="flex items-center justify-between border-b border-slate-200 px-6 py-5 dark:border-white/10">
            <div><h2 className="font-semibold">Organization API keys</h2><p className="mt-1 text-xs text-slate-400">Credentials created by your account</p></div>
            <StatusPill tone={activeKeys ? 'green' : 'slate'}>{activeKeys} active</StatusPill>
          </div>
          {keysLoading ? <div className="grid min-h-52 place-items-center"><span className="h-7 w-7 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]" /></div>
            : keysError ? <div className="p-6 text-sm text-red-600 dark:text-red-300">{keysError}</div>
              : keys.length ? <div className="overflow-x-auto">
                <table className="w-full text-left">
                  <thead className="border-b border-slate-200 bg-slate-50/80 text-[10px] font-bold uppercase tracking-[.12em] text-slate-500 dark:border-white/10 dark:bg-white/[.025] dark:text-slate-400">
                    <tr><th className="px-6 py-3.5">Key</th><th className="px-4 py-3.5">Created</th><th className="px-4 py-3.5">Last used</th><th className="px-6 py-3.5 text-right">Status</th></tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-white/[.06]">
                    {keys.map(key => <tr key={key.id} className="transition hover:bg-blue-50/40 dark:hover:bg-blue-400/[.04]">
                      <td className="px-6 py-4"><div className="flex items-center gap-3"><span className="grid h-9 w-9 shrink-0 place-items-center rounded-[10px] bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10 dark:text-blue-300"><KeyRound className="h-4 w-4" /></span><div><p className="text-sm font-semibold text-slate-900 dark:text-white">{key.name}</p><code className="mt-0.5 block text-xs text-slate-500 dark:text-slate-400">{key.visibleKey}</code></div></div></td>
                      <td className="px-4 py-4 text-xs text-slate-600 dark:text-slate-300">{formatDate(key.createdDate)}</td>
                      <td className="px-4 py-4 text-xs text-slate-600 dark:text-slate-300">{key.lastUsedDate ? formatDate(key.lastUsedDate) : 'Not used yet'}</td>
                      <td className="px-6 py-4 text-right"><StatusPill tone={key.active ? 'green' : 'slate'}>{key.active ? 'Active' : 'Disabled'}</StatusPill></td>
                    </tr>)}
                  </tbody>
                </table>
                <div className="flex items-center justify-between border-t border-slate-100 bg-slate-50/60 px-6 py-3.5 text-xs text-slate-500 dark:border-white/[.06] dark:bg-white/[.02] dark:text-slate-400"><span>{keys.length} credential{keys.length === 1 ? '' : 's'}</span><a href="/Identity/Account/Manage/ApiKeys" className="font-semibold text-[#0b5cff] dark:text-blue-300">Create or revoke keys →</a></div>
              </div>
                : <div className="p-6"><div className="rounded-xl border border-dashed border-slate-300 p-8 text-center dark:border-white/15"><span className="mx-auto grid h-12 w-12 place-items-center rounded-xl bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><KeyRound className="h-5 w-5" /></span><h3 className="mt-4 font-semibold">Create your first API key</h3><p className="mx-auto mt-2 max-w-md text-sm leading-6 text-slate-500 dark:text-slate-400">Give the key a recognizable name and optional expiration date.</p><a href="/Identity/Account/Manage/ApiKeys" className="mt-5 inline-flex rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white">Open key manager</a></div></div>}
        </Panel>
        <Panel className="border-[#172b49] bg-gradient-to-br from-[#0d1d35] to-[#07101f] p-6 text-white shadow-[0_18px_45px_rgba(4,12,26,.18)] dark:border-blue-300/15">
          <span className="grid h-10 w-10 place-items-center rounded-[10px] bg-[#86efcd]/10 text-[#86efcd]"><TerminalSquare className="h-5 w-5" /></span>
          <h2 className="mt-4 font-semibold text-white">Record metered usage</h2>
          <p className="mt-2 text-sm leading-6 text-slate-200">Use a unique idempotency key for every business operation.</p>
          <pre className="mt-5 overflow-x-auto rounded-xl border border-white/10 bg-[#030914]/75 p-4 text-[11px] leading-6 text-blue-100 shadow-inner"><code>{curl}</code></pre>
          <div className="mt-5 flex items-center gap-2 text-xs font-medium text-slate-300"><LockKeyhole className="h-4 w-4 text-[#86efcd]" />Raw secrets are never listed again</div>
        </Panel>
      </div>
    </FeatureGate>
    <Panel className="mt-5 p-6"><div className="flex items-start gap-3"><Code2 className="mt-0.5 h-5 w-5 text-[#0b5cff]" /><div><h2 className="font-semibold">Typed API contracts</h2><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Regenerate the TypeScript DTO client after changing C# request contracts with <code className="rounded bg-slate-100 px-1.5 py-1 text-xs dark:bg-white/10">npm run dtos</code>. Explore every endpoint in <a className="font-semibold text-[#0b5cff]" href="/scalar/v1">Scalar API Reference</a>.</p></div></div></Panel>
  </AppShell>
}

export default ValidateAuth(ApiKeysPage)
