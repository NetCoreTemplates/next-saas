'use client'

import Link from 'next/link'
import { ArrowUpRight, Bell, BookOpenCheck, CheckCircle2, Clock3, CreditCard, HardDrive, KeyRound } from 'lucide-react'
import AppShell, { Meter, PageHeading, Panel, StatusPill, quotaTone } from '@/components/app-shell'
import { ValidateAuth } from '@/lib/auth'
import { WorkspaceKind } from '@/lib/dtos'
import { LoadingPanel, useSaasDashboard, useUsageAnalytics } from '@/lib/use-saas'

const storageLabel = (value?:number) => { const n = value ?? 0; return n >= 1024**3 ? `${(n/1024**3).toFixed(1)} GB` : n >= 1024**2 ? `${(n/1024**2).toFixed(1)} MB` : `${n.toLocaleString()} B` }
const greeting = () => { const h = new Date().getHours(); return h < 12 ? 'Good morning' : h < 18 ? 'Good afternoon' : 'Good evening' }
const shortDate = (value?:string) => value ? new Date(value).toLocaleDateString(undefined, { month:'short', day:'numeric' }) : '—'
const healthLabel = { green:'Healthy', amber:'Approaching limit', red:'Limit reached' } as const

function DashboardPage() {
  const {data,error,loading}=useSaasDashboard()
  const {data:activity,loading:activityLoading}=useUsageAnalytics('api.requests',14,!loading)
  if (loading) return <AppShell><LoadingPanel/></AppShell>
  if (error) return <AppShell><Panel className="p-6 text-sm text-red-600">{error}</Panel></AppShell>

  const meter=(key:string)=>data?.usage?.find(x=>x.meterKey===key)
  const documents=meter('documents.stored'), storage=meter('storage.bytes'), api=meter('api.requests'), seats=meter('workspace.seats')
  const individual=data?.workspace?.kind===WorkspaceKind.Individual
  const subscription=data?.subscription
  const free=subscription?.status==='Free'
  const daysLeft=subscription?.periodEnd?Math.max(0,Math.ceil((new Date(subscription.periodEnd).getTime()-Date.now())/86400000)):undefined
  const periodDays=subscription?.periodStart&&subscription?.periodEnd?Math.max(1,Math.round((new Date(subscription.periodEnd).getTime()-new Date(subscription.periodStart).getTime())/86400000)):undefined
  const apiTone=quotaTone(api?.percentUsed)
  const series=activity?.series??[]
  const peak=Math.max(1,...series.map(x=>x.units??0))
  const total=series.reduce((sum,x)=>sum+(x.units??0),0)

  const checklist=[
    { label:individual?'Account created':'Organization created', done:true },
    { label:'Upload your first document', done:(documents?.usedUnits??0)>0, href:'/documents' },
    ...(individual?[]:[{ label:'Invite your team', done:(seats?.usedUnits??0)>1, href:'/team/invite' }]),
    { label:'Choose a paid plan', done:!free, href:'/pricing' },
  ]
  const completed=checklist.filter(x=>x.done).length

  const stats=[
    { label:'Plan', value:data?.plan?.name||'Free', icon:CreditCard, meta:<StatusPill tone={free?'slate':'blue'}>{subscription?.status}</StatusPill> },
    { label:'Documents', value:documents?.usedUnits?.toLocaleString()||'0', icon:BookOpenCheck, meta:documents?.allowance!=null?<Meter percent={documents.percentUsed} label="Documents stored" className="h-1.5"/>:null, sub:`of ${documents?.allowance?.toLocaleString()??'unlimited'} stored` },
    { label:'Storage', value:storageLabel(storage?.usedUnits), icon:HardDrive, meta:storage?.allowance!=null?<Meter percent={storage.percentUsed} label="Storage used" className="h-1.5"/>:null, sub:`of ${storage?.allowance!=null?storageLabel(storage.allowance):'unlimited'}` },
    { label:'Billing period', value:daysLeft!=null?`${daysLeft} ${daysLeft===1?'day':'days'} left`:'—', icon:Clock3, meta:periodDays&&daysLeft!=null?<Meter percent={((periodDays-daysLeft)/periodDays)*100} tone="green" label="Billing period elapsed" className="h-1.5"/>:null, sub:free?`Resets ${shortDate(subscription?.periodEnd)}`:`Renews ${shortDate(subscription?.periodEnd)}` },
  ]

  return <AppShell workspaceName={data?.workspace?.name}>
    <PageHeading eyebrow={individual?'Account overview':'Organization overview'} title={`${greeting()}, ${data?.workspace?.name || 'team'}`} description="Your documents, API usage, storage, and plan at a glance."
      action={<div className="flex gap-2">
        <Link href="/notifications" aria-label={data?.unreadNotifications?`Notifications, ${data.unreadNotifications} unread`:'Notifications'} className="relative grid h-10 w-10 place-items-center rounded-[10px] border border-slate-200 bg-white text-slate-500 transition hover:text-slate-900 dark:border-white/10 dark:bg-white/5 dark:hover:text-white"><Bell className="h-4 w-4"/>{!!data?.unreadNotifications&&<span className="absolute -right-1 -top-1 grid h-5 min-w-5 place-items-center rounded-full bg-red-500 px-1 text-[9px] font-bold text-white">{data.unreadNotifications}</span>}</Link>
        <Link href="/documents" className="inline-flex items-center gap-2 rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-[#084dcc]">Open documents <ArrowUpRight className="h-4 w-4"/></Link>
      </div>}/>

    {subscription?.accessMode&&subscription.accessMode!=='Full'&&<Panel className={`mb-5 p-4 text-sm ${subscription.accessMode==='Grace'?'border-amber-200 bg-amber-50 text-amber-800 dark:border-amber-400/20 dark:bg-amber-400/10 dark:text-amber-200':'border-red-200 bg-red-50 text-red-700 dark:border-red-400/20 dark:bg-red-400/10 dark:text-red-200'}`}><strong>{subscription.accessMode} access.</strong> {subscription.accessReason} <Link href="/billing" className="font-semibold underline underline-offset-2">Review billing</Link></Panel>}

    <div className="grid grid-cols-2 gap-3 sm:gap-5 xl:grid-cols-4">{stats.map(({label,value,icon:Icon,meta,sub})=><Panel key={label} className="flex flex-col p-4 sm:p-5">
      <div className="flex items-center justify-between"><span className="text-xs font-medium text-slate-500">{label}</span><Icon className="h-4 w-4 text-slate-400"/></div>
      <p className="tabular mt-4 truncate text-xl font-semibold tracking-[-.04em] text-slate-950 sm:mt-5 sm:text-2xl dark:text-white">{value}</p>
      <div className="mt-auto pt-3">{meta}{sub&&<p className="mt-2 truncate text-xs text-slate-400">{sub}</p>}</div>
    </Panel>)}</div>

    <div className="mt-5 grid gap-5 xl:grid-cols-[1.35fr_.65fr]">
      <Panel className="overflow-hidden">
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4 sm:px-6 sm:py-5 dark:border-white/10"><div><h2 className="font-semibold tracking-[-.02em]">API activity</h2><p className="mt-1 text-xs text-slate-400">Requests per day, last 14 days</p></div><Link href="/usage" className="text-xs font-semibold text-[#0b5cff] hover:underline dark:text-blue-300">View usage</Link></div>
        <div className="p-5 sm:p-6">
          <div className="flex items-end justify-between gap-4"><div><p className="tabular text-3xl font-semibold tracking-[-.04em]">{api?.percentUsed??0}%</p><p className="mt-1 text-xs text-slate-400">{api?.allowance!=null?`${(api.usedUnits??0).toLocaleString()} of ${api.allowance.toLocaleString()} requests this period`:'of API request allowance used'}</p></div><StatusPill tone={apiTone}>{healthLabel[apiTone as keyof typeof healthLabel]}</StatusPill></div>
          <Meter percent={api?.percentUsed} label="API requests used" className="mt-6 h-2.5"/>
          {activityLoading?<div className="mt-7 h-36 animate-pulse rounded-xl bg-slate-100 dark:bg-white/5"/>
            :total===0?<div className="mt-7 grid h-36 place-items-center rounded-xl border border-dashed border-slate-200 text-center dark:border-white/10"><div className="px-6"><p className="text-sm font-medium text-slate-700 dark:text-slate-200">No API requests in the last 14 days</p><p className="mt-1 text-xs text-slate-400">Calls made with an <Link href="/api-keys" className="text-[#0b5cff] hover:underline dark:text-blue-300">API key</Link> appear here within seconds.</p></div></div>
            :<><div className="mt-7 flex h-36 items-end gap-1.5 border-b border-slate-200 dark:border-white/10">{series.map(point=><div key={point.date} title={`${shortDate(point.date)}: ${(point.units??0).toLocaleString()} requests`} className="group flex h-full min-w-0 flex-1 items-end"><span className="w-full rounded-t-[4px] bg-blue-100 transition group-hover:bg-[#0b5cff] dark:bg-blue-400/20 dark:group-hover:bg-blue-400" style={{height:`${Math.max(point.units?4:0,((point.units??0)/peak)*100)}%`}}/></div>)}</div>
              <div className="mt-2 flex justify-between text-[10px] text-slate-400"><span>{shortDate(series[0]?.date)}</span><span className="tabular">{total.toLocaleString()} requests</span><span>Today</span></div></>}
        </div>
      </Panel>

      <Panel className="p-5 sm:p-6">
        <div className="flex items-center justify-between"><h2 className="font-semibold tracking-[-.02em]">Getting started</h2><span className="tabular text-xs font-semibold text-slate-400">{completed} of {checklist.length}</span></div>
        <Meter percent={(completed/checklist.length)*100} tone="green" label="Getting started progress" className="mt-4 h-1.5"/>
        <ol className="mt-6 space-y-4">{checklist.map((step,i)=><li key={step.label} className="flex items-start gap-3">
          <span className={`grid h-6 w-6 shrink-0 place-items-center rounded-full text-xs font-bold ${step.done?'bg-emerald-100 text-emerald-700 dark:bg-emerald-400/10 dark:text-[#86efcd]':'border border-slate-200 text-slate-400 dark:border-white/10'}`}>{step.done?<CheckCircle2 className="h-4 w-4"/>:i+1}</span>
          <div className="min-w-0 pt-0.5">{step.done||!step.href?<p className={`text-sm font-medium ${step.done?'text-slate-400 line-through decoration-slate-300 dark:decoration-slate-600':''}`}>{step.label}</p>:<Link href={step.href} className="group text-sm font-medium hover:text-[#0b5cff] dark:hover:text-blue-300">{step.label} <span className="text-[#0b5cff] transition group-hover:translate-x-0.5 dark:text-blue-300">→</span></Link>}</div>
        </li>)}</ol>
        <div className="mt-7 rounded-xl bg-[#091426] p-4 text-white"><KeyRound className="h-5 w-5 text-[#86efcd]"/><p className="mt-3 text-sm font-semibold">Automate document imports</p><p className="mt-1 text-xs leading-5 text-slate-400">Use API keys and typed contracts to connect your content pipeline.</p><a href="/scalar/v1" className="mt-3 inline-flex text-xs font-semibold text-[#86efcd] hover:underline">Open API reference →</a></div>
      </Panel>
    </div>
  </AppShell>
}

export default ValidateAuth(DashboardPage)
