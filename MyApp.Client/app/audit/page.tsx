'use client'

import { useEffect, useRef, useState } from 'react'
import { CheckCircle2, Clock3, Download, Filter, ScrollText, ShieldAlert, UserRound } from 'lucide-react'
import AppShell, { PageHeading, Panel, StatusPill } from '@/components/app-shell'
import FeatureGate, { hasFeature } from '@/components/feature-gate'
import { ValidateAuth } from '@/lib/auth'
import { client } from '@/lib/gateway'
import { ExportWorkspaceAuditCsv, QueryWorkspaceAuditEvents, SaasAuditEvent } from '@/lib/dtos'
import { LoadingPanel, useSaasDashboard } from '@/lib/use-saas'

function AuditPage(){
  const {data,loading:dashboardLoading}=useSaasDashboard()
  const [events,setEvents]=useState<SaasAuditEvent[]>([])
  const [loading,setLoading]=useState(false)
  const [error,setError]=useState<string>()
  const [filter,setFilter]=useState('')
  const [exporting,setExporting]=useState(false)
  const loaded=useRef(false)
  useEffect(()=>{
    if(dashboardLoading||loaded.current||!hasFeature(data?.entitlements,'audit.read'))return
    loaded.current=true;setLoading(true)
    client.api(new QueryWorkspaceAuditEvents({take:200})).then(api=>{if(api.succeeded)setEvents(api.response?.results??[]);else setError(api.error?.message??'Unable to load the audit log.');setLoading(false)})
  },[dashboardLoading,data?.entitlements])
  if(dashboardLoading)return <AppShell><LoadingPanel/></AppShell>
  const shown=events.filter(x=>!filter||x.action?.toLowerCase().includes(filter.toLowerCase())||x.category?.toLowerCase().includes(filter.toLowerCase())||x.actorId?.toLowerCase().includes(filter.toLowerCase()))
  const exportCsv=async()=>{setExporting(true);setError(undefined);try{const blob=await client.get(new ExportWorkspaceAuditCsv({days:90}));const url=URL.createObjectURL(blob);const link=document.createElement('a');link.href=url;link.download=`audit-${new Date().toISOString().slice(0,10)}.csv`;link.click();URL.revokeObjectURL(url)}catch(e){setError(e instanceof Error?e.message:'Unable to export the audit log.')}finally{setExporting(false)}}
  return <AppShell workspaceName={data?.workspace?.name}>
    <PageHeading eyebrow="Accountability & compliance" title="Audit log" description="A record of plan, quota, billing, membership, file, support, and organization changes." action={<button onClick={exportCsv} disabled={exporting||!hasFeature(data?.entitlements,'audit.read')} className="inline-flex items-center gap-2 rounded-[10px] border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 disabled:opacity-50 dark:border-white/15 dark:bg-white/5 dark:text-white"><Download className="h-4 w-4"/>{exporting?'Exporting…':'Export 90 days'}</button>}/>
    <FeatureGate feature="audit.read" entitlements={data?.entitlements} title="Audit history is not included in this plan">
      <Panel className="overflow-hidden"><div className="flex flex-col gap-4 border-b border-slate-200 px-6 py-5 sm:flex-row sm:items-center sm:justify-between dark:border-white/10"><div className="flex items-center gap-3"><span className="grid h-10 w-10 place-items-center rounded-xl bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><ScrollText className="h-5 w-5"/></span><div><h2 className="font-semibold">Organization activity</h2><p className="mt-1 text-xs text-slate-400">{events.length} retained events</p></div></div><label className="relative"><Filter className="absolute left-3 top-2.5 h-4 w-4 text-slate-400"/><input value={filter} onChange={e=>setFilter(e.target.value)} placeholder="Filter action, category, actor" className="w-full rounded-[10px] border-slate-200 bg-white py-2 pl-9 pr-3 text-sm dark:border-white/10 dark:bg-white/5 sm:w-72"/></label></div>
        {error&&<div className="border-b border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-400/20 dark:bg-red-400/10 dark:text-red-200">{error}</div>}
        {loading?<div className="grid min-h-64 place-items-center"><span className="h-7 w-7 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]"/></div>:shown.length?<div className="divide-y divide-slate-100 dark:divide-white/5">{shown.map(event=><div key={event.id} className="grid gap-4 px-6 py-4 md:grid-cols-[minmax(0,1.2fr)_minmax(0,.8fr)_auto]"><div className="flex min-w-0 gap-3"><span className={`mt-0.5 grid h-9 w-9 shrink-0 place-items-center rounded-lg ${event.outcome==='Failed'?'bg-red-50 text-red-600 dark:bg-red-400/10':'bg-emerald-50 text-emerald-600 dark:bg-emerald-400/10'}`}>{event.outcome==='Failed'?<ShieldAlert className="h-4 w-4"/>:<CheckCircle2 className="h-4 w-4"/>}</span><div className="min-w-0"><p className="truncate text-sm font-semibold">{event.action}</p><div className="mt-1 flex flex-wrap gap-2"><StatusPill tone="slate">{event.category}</StatusPill>{event.outcome&&<StatusPill tone={event.outcome==='Failed'?'amber':'green'}>{event.outcome}</StatusPill>}</div></div></div><div className="text-xs text-slate-500 dark:text-slate-400"><p className="flex items-center gap-2"><UserRound className="h-3.5 w-3.5"/><span className="truncate font-mono">{event.actorId||'system'}</span></p>{event.reason&&<p className="mt-2 line-clamp-2">{event.reason}</p>}<p className="mt-2 truncate font-mono text-[10px] text-slate-400">{event.requestId}</p></div><p className="flex items-center gap-2 whitespace-nowrap text-[11px] text-slate-400"><Clock3 className="h-3.5 w-3.5"/>{event.createdDate?new Date(event.createdDate).toLocaleString():'—'}</p></div>)}</div>:<div className="grid min-h-64 place-items-center p-8 text-center"><div><ScrollText className="mx-auto h-7 w-7 text-slate-300"/><h3 className="mt-4 font-semibold">No matching activity</h3><p className="mt-2 text-sm text-slate-500">Events appear as organization activity occurs.</p></div></div>}
      </Panel>
    </FeatureGate>
  </AppShell>
}
export default ValidateAuth(AuditPage)
