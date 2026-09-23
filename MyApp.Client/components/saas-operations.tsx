'use client'

import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { Activity, Archive, BarChart3, BellRing, CheckCircle2, Clock3, DatabaseZap, Download, FileText, Headphones, RefreshCw, Search, ShieldCheck, UsersRound, X } from 'lucide-react'
import { Panel, StatusPill } from '@/components/app-shell'
import { appAuth } from '@/lib/auth'
import { client } from '@/lib/gateway'
import { AdjustCustomerGauge, ChangeWorkspaceStatus, CreateSupportAccessGrant, CreateSupportNote, DeleteCustomerOverride, EndSupportAccess, ExportPlatformAuditCsv, GetSaasAnalytics, GetSaasAnalyticsResponse, GetSaasCustomer, GetSaasOperations, GetSaasOperationsResponse, PlatformOperationType, PlatformOperatorInfo, PreviewSaasCustomerOperation, PreviewSaasCustomerOperationResponse, QueryPlatformAuditEvents, QueryPlatformOperators, QuerySaasCustomers, ReconcileSaasCustomerBilling, RetryNotificationDelivery, RetryStripeEvent, RetryWorkspaceLifecycle, RevokeSupportAccessGrant, SaasAuditEvent, SaasCustomerDetails, SaasCustomerSummary, StartSupportAccess, UpdateWorkspaceRetentionPolicy, WorkspaceStatus } from '@/lib/dtos'

const fmt=(n?:number)=>(n??0).toLocaleString()
const metricValue=(value?:number,format?:string)=>format?.startsWith('currency:')?new Intl.NumberFormat(undefined,{style:'currency',currency:format.slice(9),maximumFractionDigits:0}).format(value??0):fmt(value)
const bytes=(n?:number)=>{const v=n??0;return v>=1024**3?`${(v/1024**3).toFixed(1)} GB`:v>=1024**2?`${(v/1024**2).toFixed(1)} MB`:`${v.toLocaleString()} B`}

type PendingOperation={preview:PreviewSaasCustomerOperationResponse;run:(confirmation:string,reason:string)=>Promise<void>}

export type SaasOperationsView = 'overview' | 'customers' | 'usage' | 'operations' | 'security' | 'settings'

export default function SaasOperations({view='overview'}:{view?:SaasOperationsView}){
  const {hasRole}=appAuth();const isAdmin=hasRole('Admin')
  const [analytics,setAnalytics]=useState<GetSaasAnalyticsResponse>();const [operations,setOperations]=useState<GetSaasOperationsResponse>();const [customer,setCustomer]=useState<SaasCustomerDetails>();const [workspaceId,setWorkspaceId]=useState('');const [busy,setBusy]=useState<string>();const [notice,setNotice]=useState<{ok:boolean,text:string}>();const [customers,setCustomers]=useState<SaasCustomerSummary[]>([]);const [search,setSearch]=useState('');const [operators,setOperators]=useState<PlatformOperatorInfo[]>([]);const [audits,setAudits]=useState<SaasAuditEvent[]>([]);const [auditSearch,setAuditSearch]=useState('');const [pending,setPending]=useState<PendingOperation>();const [confirmation,setConfirmation]=useState('');const [reason,setReason]=useState('')
  const needsAnalytics=isAdmin&&(view==='overview'||view==='usage')
  const needsOperations=view==='customers'||view==='operations'||view==='security'
  const needsCustomers=view==='customers'||view==='security'
  const load=useCallback(async()=>{await Promise.all([
    needsAnalytics?client.api(new GetSaasAnalytics({days:30})).then(api=>{if(api.succeeded)setAnalytics(api.response)}):Promise.resolve(),
    needsOperations?client.api(new GetSaasOperations()).then(api=>{if(api.succeeded)setOperations(api.response)}):Promise.resolve(),
    needsCustomers?client.api(new QuerySaasCustomers({take:50})).then(api=>{if(api.succeeded)setCustomers(api.response?.results??[])}):Promise.resolve(),
  ])},[needsAnalytics,needsOperations,needsCustomers])
  useEffect(()=>{void load()},[load])
  useEffect(()=>{if(view!=='customers'||!operations?.capabilities?.canApproveSupportAccess)return;void client.api(new QueryPlatformOperators()).then(api=>{if(api.succeeded)setOperators(api.response?.results??[])})},[view,operations?.capabilities?.canApproveSupportAccess])
  useEffect(()=>{if(view!=='security'||!operations?.capabilities?.canManagePlatform)return;void client.api(new QueryPlatformAuditEvents({take:50})).then(api=>{if(api.succeeded)setAudits(api.response?.results??[])})},[view,operations?.capabilities?.canManagePlatform])
  useEffect(()=>{const expires=customer?.supportAccess?.expiresAt;if(!expires)return;const delay=new Date(expires).getTime()-Date.now();if(delay<=0){setCustomer(undefined);return}const timer=window.setTimeout(()=>setCustomer(undefined),Math.min(delay,2_147_483_647));return()=>window.clearTimeout(timer)},[customer?.supportAccess?.expiresAt])
  const findCustomers=async(e?:FormEvent)=>{e?.preventDefault();const api=await client.api(new QuerySaasCustomers({search:search||undefined,take:50}));if(api.succeeded)setCustomers(api.response?.results??[]);else setNotice({ok:false,text:api.error?.message??'Unable to search customers.'})}
  const loadCustomer=async(id:string)=>{setWorkspaceId(id);setCustomer(undefined);if(!id)return;const api=await client.api(new GetSaasCustomer({workspaceId:id}));if(api.succeeded)setCustomer(api.response);else setNotice({ok:false,text:api.error?.message??'Unable to load customer.'})}
  const operation=async(key:string,task:()=>Promise<{succeeded:boolean,error?:{message?:string}}>,success:string,reloadCustomer=false)=>{setBusy(key);setNotice(undefined);const api=await task();setNotice(api.succeeded?{ok:true,text:success}:{ok:false,text:api.error?.message??'Operation failed.'});setBusy(undefined);if(api.succeeded){if(key==='end-access')setCustomer(undefined);await load();if(reloadCustomer&&workspaceId)await loadCustomer(workspaceId)}}
  const note=async(e:FormEvent<HTMLFormElement>)=>{e.preventDefault();const form=e.currentTarget;const data=new FormData(form);await operation('note',()=>client.api(new CreateSupportNote({workspaceId,body:String(data.get('body'))})),'Private support note added.',true);form.reset()}
  const access=async(e:FormEvent<HTMLFormElement>)=>{e.preventDefault();const form=e.currentTarget;const data=new FormData(form);await operation('access',()=>client.api(new CreateSupportAccessGrant({workspaceId,operatorId:String(data.get('operatorId')),reason:String(data.get('reason')),minutes:Number(data.get('minutes'))})),'Time-boxed read-only support access approved.');form.reset()}
  const previewOperation=async(request:PreviewSaasCustomerOperation,run:PendingOperation['run'])=>{setBusy('preview');const api=await client.api(request);setBusy(undefined);if(!api.succeeded||!api.response){setNotice({ok:false,text:api.error?.message??'Unable to preview operation.'});return}setConfirmation('');setReason('');setPending({preview:api.response,run})}
  const correction=async(e:FormEvent<HTMLFormElement>)=>{e.preventDefault();const form=e.currentTarget;const data=new FormData(form);const meterKey=String(data.get('meterKey'));const delta=Number(data.get('delta'));await previewOperation(new PreviewSaasCustomerOperation({workspaceId,operation:PlatformOperationType.GaugeAdjustment,meterKey,delta}),async(confirm,why)=>{await operation('correction',()=>client.api(new AdjustCustomerGauge({workspaceId,meterKey,delta,reason:why,idempotencyKey:crypto.randomUUID(),confirmation:confirm})),'Gauge corrected and recorded in the customer audit trail.',true);form.reset()})}
  const saveRetention=async(e:FormEvent<HTMLFormElement>)=>{e.preventDefault();const data=new FormData(e.currentTarget);const days=(key:string)=>{const value=String(data.get(key)??'').trim();return value?Number(value):undefined};await operation('retention',()=>client.api(new UpdateWorkspaceRetentionPolicy({workspaceId,analyticsRetentionDays:days('analytics'),auditRetentionDays:days('audit'),notificationRetentionDays:days('notifications'),deletedFileRetentionDays:days('files'),lifecycleHistoryRetentionDays:days('lifecycle'),legalHold:data.get('legalHold')==='on',reason:String(data.get('reason')??'')})),'Retention policy updated.',true)}
  const maxGrowth=useMemo(()=>Math.max(1,...(analytics?.workspaceGrowth?.map(x=>x.units??0)??[])),[analytics])
  return <div className="mb-5 space-y-5">
    {notice&&<Panel className={`p-4 text-sm ${notice.ok?'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-400/20 dark:bg-emerald-400/10 dark:text-emerald-200':'border-red-200 bg-red-50 text-red-700 dark:border-red-400/20 dark:bg-red-400/10 dark:text-red-200'}`}>{notice.text}</Panel>}
    {((view==='overview'&&isAdmin)||view==='usage')&&<div className="grid gap-5 xl:grid-cols-[1.3fr_.7fr]">
<Panel className="p-6">
<div className="flex items-center justify-between">
<div>
<h2 className="font-semibold">Business pulse</h2>
<p className="mt-1 text-xs text-slate-400">Trailing 30 days · derived from local billing and usage state</p>
</div>
<BarChart3 className="h-5 w-5 text-[#0b5cff]"/>
</div>
<div className="mt-6 grid gap-3 sm:grid-cols-3">{analytics?.metrics?.map(metric=>
<div key={metric.key} className="rounded-xl bg-slate-50 p-4 dark:bg-white/[.035]">
<p className="text-xl font-semibold tracking-[-.04em]">{metricValue(metric.value,metric.format)}</p>
<p className="mt-1 text-[11px] text-slate-400">{metric.label}</p>
</div>)}</div>
<div className="mt-6 flex h-24 items-end gap-1">{analytics?.workspaceGrowth?.map(point=>
<span key={point.date} title={`${point.date}: ${point.units}`} className="min-w-0 flex-1 rounded-t-sm bg-blue-100 dark:bg-blue-400/15" style={{height:`${Math.max(point.units?8:2,((point.units??0)/maxGrowth)*100)}%`}}/>)}</div>
</Panel>
      <Panel className="p-6">
<div className="flex items-center justify-between">
<div>
<h2 className="font-semibold">Plan mix</h2>
<p className="mt-1 text-xs text-slate-400">Pinned subscription versions</p>
</div>
<UsersRound className="h-5 w-5 text-[#0b5cff]"/>
</div>
<div className="mt-6 space-y-4">{analytics?.planMix?.map(item=>
<div key={item.key}>
<div className="flex justify-between text-xs">
<span>{item.label}</span>
<strong>{fmt(item.units)}</strong>
</div>
<div className="mt-2 h-2 overflow-hidden rounded-full bg-slate-100 dark:bg-white/10">
<div className="meter-fill h-full rounded-full bg-gradient-to-r from-[#0b5cff] to-[#86efcd]" style={{width:`${Math.max(3,((item.units??0)/Math.max(1,...(analytics?.planMix??[]).map(x=>x.units??0)))*100)}%`}}/>
</div>
</div>)}</div>
<div className="mt-6 flex items-center justify-between rounded-xl bg-amber-50 p-4 text-amber-800 dark:bg-amber-400/10 dark:text-amber-200">
<span className="text-xs font-semibold">Quota pressure</span>
<strong>{analytics?.quotaPressure?.length??0} {(analytics?.quotaPressure?.length??0)===1?'organization':'organizations'}</strong>
</div>
</Panel>
</div>}
    {view==='customers'&&<Panel className="overflow-hidden">
<div className="flex flex-col gap-4 border-b border-slate-200 px-6 py-5 lg:flex-row lg:items-center lg:justify-between dark:border-white/10">
<div>
<h2 className="font-semibold">Customer 360</h2>
<p className="mt-1 text-xs text-slate-400">Search organization, member email, Stripe identifiers, or API-key fingerprint.</p>
</div>
<div className="flex flex-col gap-2 sm:flex-row">
<form onSubmit={findCustomers} className="relative">
<Search className="absolute left-3 top-2.5 h-4 w-4 text-slate-400"/>
<input value={search} onChange={e=>setSearch(e.target.value)} placeholder="Find a customer" className="w-full rounded-[10px] border-slate-200 bg-white py-2 pl-9 pr-3 text-sm dark:border-white/10 dark:bg-[#0c1729] sm:w-64"/>
</form>
<select value={workspaceId} onChange={e=>void loadCustomer(e.target.value)} className="w-full rounded-[10px] border-slate-200 bg-white py-2 px-3 text-sm dark:border-white/10 dark:bg-[#0c1729] sm:w-72">
<option value="">Select an organization</option>{customers.map(x=>
<option key={x.workspaceId} value={x.workspaceId}>{x.name} · {x.planName}
{x.matchedOn?` · ${x.matchedOn}`:''}</option>)}</select>
</div>
</div>
      {!workspaceId?<div className="grid min-h-52 place-items-center p-8 text-center">
<div>
<Search className="mx-auto h-7 w-7 text-slate-300"/>
<p className="mt-3 text-sm text-slate-500">Choose an organization to inspect its effective state.</p>
</div>
</div>:!customer?<div className="grid min-h-52 place-items-center">
<span className="h-7 w-7 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]"/>
</div>:<div className="p-6">
<div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">{[
        ['Plan',customer.plan?.name??'—',ShieldCheck],['Access',customer.subscription?.accessMode??'—',CheckCircle2],['Members',fmt(customer.members?.length),UsersRound],['Documents',fmt(customer.files?.length),FileText],['Audit events',fmt(customer.auditEvents?.length),Activity]
      ].map(([label,value,Icon])=>
<div key={label as string} className="rounded-xl bg-slate-50 p-4 dark:bg-white/[.035]">
<Icon className="h-4 w-4 text-[#0b5cff]"/>
<p className="mt-3 text-lg font-semibold">{value as string}</p>
<p className="text-[11px] text-slate-400">{label as string}</p>
</div>)}</div>
      {customer.supportAccess&&<div className="mt-5 flex flex-col gap-3 rounded-xl border border-blue-200 bg-blue-50 p-4 text-blue-900 sm:flex-row sm:items-center dark:border-blue-400/20 dark:bg-blue-400/10 dark:text-blue-100">
<ShieldCheck className="h-5 w-5 shrink-0"/>
<div className="min-w-0 flex-1">
<p className="text-sm font-semibold">Read-only support session</p>
<p className="mt-1 text-xs opacity-75">Approved until {customer.supportAccess.expiresAt?new Date(customer.supportAccess.expiresAt).toLocaleString():'—'} · {customer.supportAccess.reason}</p>
</div>
<button onClick={()=>customer.supportAccess?.id&&void operation('end-access',()=>client.api(new EndSupportAccess({id:customer.supportAccess!.id})),'Support session ended.')} className="rounded-[9px] border border-blue-300 px-3 py-2 text-xs font-semibold dark:border-blue-300/20">End session</button>
</div>}
      {customer.capabilities?.canManagePlatform&&<form key={`retention-${workspaceId}`} onSubmit={saveRetention} className="mt-5 rounded-xl border border-slate-200 p-5 dark:border-white/10">
<div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
<div>
<h3 className="text-sm font-semibold">Data retention</h3>
<p className="mt-1 text-xs leading-5 text-slate-400">Blank periods inherit deployment defaults. Legal holds suspend retention and organization deletion.</p>
</div>
<label className="inline-flex items-center gap-2 text-xs font-semibold">
<input name="legalHold" type="checkbox" defaultChecked={customer.retentionPolicy?.legalHold} disabled={!operations?.retention?.enableLegalHolds} className="rounded border-slate-300 text-[#0b5cff]"/>Legal hold</label>
</div>
<div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">{[['analytics','Analytics',customer.retentionPolicy?.analyticsRetentionDays,operations?.retention?.analyticsRetentionDays],['audit','Audit',customer.retentionPolicy?.auditRetentionDays,operations?.retention?.auditRetentionDays],['notifications','Notifications',customer.retentionPolicy?.notificationRetentionDays,operations?.retention?.notificationRetentionDays],['files','Deleted files',customer.retentionPolicy?.deletedFileRetentionDays,operations?.retention?.deletedFileRetentionDays],['lifecycle','Lifecycle history',customer.retentionPolicy?.lifecycleHistoryRetentionDays,operations?.retention?.lifecycleHistoryRetentionDays]].map(([name,label,value,fallback])=>
<label key={name as string} className="text-[10px] font-semibold text-slate-500">{label as string}<input name={name as string} type="number" min="1" max="3650" defaultValue={value as number|undefined} placeholder={`${fallback} days`} className="mt-1 block w-full rounded-[9px] border-slate-200 bg-white text-xs dark:border-white/10 dark:bg-white/5"/>
</label>)}</div>
<div className="mt-3 flex flex-col gap-2 sm:flex-row">
<input name="reason" required defaultValue={customer.retentionPolicy?.reason} placeholder="Required business or compliance reason" className="min-w-0 flex-1 rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-white/5"/>
<button disabled={!!busy} className="rounded-[10px] bg-[#0b5cff] px-4 py-2 text-xs font-semibold text-white disabled:opacity-40">Save policy</button>
</div>
</form>}
      <div className="mt-5 grid gap-5 xl:grid-cols-2">
<div className="rounded-xl border border-slate-200 p-5 dark:border-white/10">
<h3 className="text-sm font-semibold">Effective quotas</h3>
<div className="mt-4 space-y-3">{customer.usage?.map(x=>
<div key={x.meterKey}>
<div className="flex items-center justify-between text-xs">
<span>{x.displayName}</span>
<strong>{x.meterKey==='storage.bytes'?bytes(x.usedUnits):fmt(x.usedUnits)} / {x.allowance==null?'∞':x.meterKey==='storage.bytes'?bytes(x.allowance):fmt(x.allowance)}</strong>
</div>
<div className="mt-2 h-1.5 overflow-hidden rounded-full bg-slate-100 dark:bg-white/10">
<div className="h-full bg-[#0b5cff]" style={{width:`${Math.min(100,x.percentUsed??0)}%`}}/>
</div>
</div>)}</div>
</div>
<div className="rounded-xl border border-slate-200 p-5 dark:border-white/10">
<div className="flex items-center justify-between">
<h3 className="text-sm font-semibold">Organization status</h3>
<StatusPill tone={customer.workspace?.status==='Active'?'green':'amber'}>{customer.workspace?.status}</StatusPill>
</div>{customer.capabilities?.canManagePlatform&&<div className="mt-4 flex gap-2">
<select id="customer-status" defaultValue={customer.workspace?.status} className="min-w-0 flex-1 rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-[#0c1729]">{[WorkspaceStatus.Active,WorkspaceStatus.Suspended,WorkspaceStatus.Archived].map(x=>
<option key={x}>{x}</option>)}</select>
<button disabled={!!busy} onClick={()=>{const input=document.getElementById('customer-status') as HTMLSelectElement;const status=input.value as WorkspaceStatus;void previewOperation(new PreviewSaasCustomerOperation({workspaceId,operation:PlatformOperationType.WorkspaceStatusChange,status}),async(confirm,why)=>operation('status',()=>client.api(new ChangeWorkspaceStatus({workspaceId,status,reason:why,confirmation:confirm})),'Organization status updated.',true))}} className="rounded-[10px] bg-[#0b5cff] px-4 py-2 text-xs font-semibold text-white">Preview</button>
</div>}<p className="mt-4 text-xs leading-5 text-slate-500 dark:text-slate-400">{customer.subscription?.accessReason||'Normal access policy applies.'}</p>{customer.capabilities?.canManageBilling&&customer.subscription?.stripeSubscriptionId&&<button disabled={!!busy||!operations?.stripeConfigured} onClick={()=>void previewOperation(new PreviewSaasCustomerOperation({workspaceId,operation:PlatformOperationType.BillingReconciliation}),async(confirm,why)=>operation('reconcile',()=>client.api(new ReconcileSaasCustomerBilling({workspaceId,confirmation:confirm,reason:why})),'Billing synchronized with Stripe.',true))} className="mt-3 inline-flex items-center gap-2 rounded-[10px] border border-slate-300 px-3 py-2 text-xs font-semibold disabled:opacity-40 dark:border-white/15">
<RefreshCw className="h-3.5 w-3.5"/>Preview Stripe sync</button>}</div>
</div>
      <div className="mt-5 grid gap-5 xl:grid-cols-[1.2fr_.8fr]">
<div className="rounded-xl border border-slate-200 p-5 dark:border-white/10">
<h3 className="text-sm font-semibold">Entitlement diagnostics</h3>
<div className="mt-4 grid gap-2 sm:grid-cols-2">{customer.entitlements?.map(x=>
<div key={x.key} className="flex items-center gap-3 rounded-lg bg-slate-50 p-3 dark:bg-white/[.035]">
<StatusPill tone={x.enabled?'green':'slate'}>{x.enabled?'On':'Off'}</StatusPill>
<div className="min-w-0">
<p className="truncate text-xs font-semibold">{x.displayName}</p>
<p className="truncate font-mono text-[9px] text-slate-400">{x.key} · {x.source}</p>
</div>
</div>)}</div>
</div>
<div className="rounded-xl border border-slate-200 p-5 dark:border-white/10">
<h3 className="text-sm font-semibold">Customer overrides</h3>
<div className="mt-4 space-y-2">{customer.overrides?.length?customer.overrides.map(x=>
<div key={x.id} className="flex items-center gap-3 rounded-lg bg-slate-50 p-3 dark:bg-white/[.035]">
<div className="min-w-0 flex-1">
<p className="truncate font-mono text-[10px] font-semibold">{x.key}</p>
<p className="mt-1 truncate text-[10px] text-slate-400">{x.enabled!=null?String(x.enabled):fmt(x.quotaUnits)} · {x.reason}</p>
</div>
<button title="Remove override" disabled={!!busy} onClick={()=>x.id&&void operation(`override-${x.id}`,()=>client.api(new DeleteCustomerOverride({id:x.id})),'Customer override removed.',true)} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-400/10">
<RefreshCw className="h-3.5 w-3.5"/>
</button>
</div>):<p className="text-xs leading-5 text-slate-400">No customer-specific exceptions. The pinned plan is authoritative.</p>}</div>
</div>
</div>
      <div className="mt-5 grid gap-5 xl:grid-cols-3">{customer.capabilities?.canManageSupport&&<form onSubmit={note} className="rounded-xl border border-slate-200 p-5 dark:border-white/10">
<div className="flex items-center gap-2">
<Headphones className="h-4 w-4 text-[#0b5cff]"/>
<h3 className="text-sm font-semibold">Private support note</h3>
</div>
<textarea name="body" required rows={3} placeholder="Record customer context, decisions, or follow-up…" className="mt-4 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-white/5"/>
<button disabled={!!busy} className="mt-3 rounded-[10px] border border-slate-300 px-4 py-2 text-xs font-semibold dark:border-white/15">Add note</button>{!!customer.supportNotes?.length&&<p className="mt-3 text-xs text-slate-400">Latest: {customer.supportNotes[0].body}</p>}</form>}
{operations?.supportAccessEnabled&&operations.capabilities?.canApproveSupportAccess&&<form onSubmit={access} className="rounded-xl border border-slate-200 p-5 dark:border-white/10">
<div className="flex items-center gap-2">
<ShieldCheck className="h-4 w-4 text-[#0b5cff]"/>
<h3 className="text-sm font-semibold">Approve support access</h3>
</div>
<select name="operatorId" required className="mt-4 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-[#0c1729]">
<option value="">Select Support operator</option>{operators.map(x=>
<option key={x.userId} value={x.userId}>{x.displayName} · {x.email}</option>)}</select>
<div className="mt-2 flex gap-2">
<input name="reason" required placeholder="Approval reason" className="min-w-0 flex-1 rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-white/5"/>
<input name="minutes" type="number" min="1" max="240" defaultValue="30" className="w-24 rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-white/5"/>
</div>
<button disabled={!!busy||!operators.length} className="mt-3 rounded-[10px] border border-slate-300 px-4 py-2 text-xs font-semibold disabled:opacity-40 dark:border-white/15">Approve read-only access</button>
</form>}
{customer.capabilities?.canManagePlatform&&<form onSubmit={correction} className="rounded-xl border border-slate-200 p-5 dark:border-white/10">
<div className="flex items-center gap-2">
<DatabaseZap className="h-4 w-4 text-[#0b5cff]"/>
<h3 className="text-sm font-semibold">Correct gauge usage</h3>
</div>
<div className="mt-4 grid grid-cols-[1fr_7rem] gap-2">
<select name="meterKey" required className="min-w-0 rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-[#0c1729]">{customer.usage?.filter(x=>x.kind==='Gauge').map(x=>
<option key={x.meterKey} value={x.meterKey}>{x.displayName}</option>)}</select>
<input name="delta" required type="number" step="1" placeholder="± units" className="rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-white/5"/>
</div>
<button disabled={!!busy||!customer.usage?.some(x=>x.kind==='Gauge')} className="mt-3 rounded-[10px] border border-slate-300 px-4 py-2 text-xs font-semibold disabled:opacity-40 dark:border-white/15">Preview correction</button>
</form>}</div>
</div>}
    </Panel>}
    {view==='operations'&&<div className="grid gap-5 xl:grid-cols-2">
<Panel className="overflow-hidden">
<div className="border-b border-slate-200 px-6 py-5 dark:border-white/10">
<div className="flex items-center justify-between">
<div>
<h2 className="font-semibold">Operations queue</h2>
<p className="mt-1 text-xs text-slate-400">Actionable asynchronous failures and pending work</p>
</div>
<BellRing className="h-5 w-5 text-[#0b5cff]"/>
</div>
</div>
<div className="divide-y divide-slate-100 dark:divide-white/5">{operations?.failedStripeEvents?.map(item=>
<div key={item.id} className="flex items-center gap-3 p-4">
<Activity className="h-4 w-4 text-red-500"/>
<div className="min-w-0 flex-1">
<p className="truncate text-xs font-semibold">Stripe · {item.eventType}</p>
<p className="truncate text-[10px] text-red-500">{item.lastError}</p>
</div>
<button title="Retry Stripe event" onClick={()=>item.id&&void operation(item.id,()=>client.api(new RetryStripeEvent({id:item.id})),'Stripe event requeued.')} className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 dark:border-white/10">
<RefreshCw className="h-3.5 w-3.5"/>
</button>
</div>)}
{operations?.failedNotifications?.map(item=>
<div key={item.id} className="flex items-center gap-3 p-4">
<BellRing className="h-4 w-4 text-red-500"/>
<div className="min-w-0 flex-1">
<p className="truncate text-xs font-semibold">{item.subject}</p>
<p className="truncate text-[10px] text-red-500">{item.lastError}</p>
</div>
<button onClick={()=>item.id&&void operation(item.id,()=>client.api(new RetryNotificationDelivery({id:item.id})),'Notification requeued.')} className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 dark:border-white/10">
<RefreshCw className="h-3.5 w-3.5"/>
</button>
</div>)}
{operations?.activeLifecycleRequests?.map(item=>
<div key={item.id} className="flex items-center gap-3 p-4">
<Clock3 className="h-4 w-4 text-amber-500"/>
<div className="min-w-0 flex-1">
<p className="text-xs font-semibold">{item.type} · {item.status}</p>
<p className="truncate text-[10px] text-slate-400">{item.workspaceId}</p>
</div>{item.status==='Failed'&&<button onClick={()=>item.id&&void operation(item.id,()=>client.api(new RetryWorkspaceLifecycle({id:item.id})),'Lifecycle job requeued.')} className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 dark:border-white/10">
<RefreshCw className="h-3.5 w-3.5"/>
</button>}</div>)}
{!operations?.failedStripeEvents?.length&&!operations?.failedNotifications?.length&&!operations?.activeLifecycleRequests?.length&&<div className="p-8 text-center">
<CheckCircle2 className="mx-auto h-6 w-6 text-emerald-500"/>
<p className="mt-3 text-sm font-semibold">No operational exceptions</p>
</div>}</div>
</Panel>
      <Panel className="p-6">
<div className="flex items-center justify-between">
<div>
<h2 className="font-semibold">Product registry</h2>
<p className="mt-1 text-xs text-slate-400">Stable keys from deployment configuration</p>
</div>
<DatabaseZap className="h-5 w-5 text-[#0b5cff]"/>
</div>
<div className="mt-5 grid gap-3 sm:grid-cols-2">{operations?.meters?.map(m=>
<div key={m.key} className="rounded-xl bg-slate-50 p-3 dark:bg-white/[.035]">
<p className="font-mono text-[10px] text-[#0b5cff]">{m.key}</p>
<p className="mt-1 text-xs font-semibold">{m.displayName}</p>
<p className="mt-1 text-[10px] text-slate-400">{m.kind} · {m.reset} · {m.aggregation}</p>
</div>)}</div>
<div className="mt-5 flex flex-wrap gap-2">{operations?.features?.map(f=>
<StatusPill key={f.key} tone="slate">{f.key}</StatusPill>)}</div>
<div className="mt-5 grid grid-cols-3 gap-2 text-center text-[10px]">
<div className="rounded-lg bg-slate-50 p-3 dark:bg-white/[.035]">
<strong className="block text-sm">{operations?.stripeConfigured?'Ready':'Setup'}</strong>Stripe</div>
<div className="rounded-lg bg-slate-50 p-3 dark:bg-white/[.035]">
<strong className="block text-sm">{operations?.emailEnabled?'On':'Off'}</strong>Email</div>
<div className="rounded-lg bg-slate-50 p-3 dark:bg-white/[.035]">
<strong className="block text-sm">{operations?.pendingReservations?.length??0}</strong>Reservations</div>
</div>
</Panel>
</div>}
    {view==='security'&&<>{operations?.capabilities?.canManagePlatform&&<Panel className="p-6">
<div className="flex items-center justify-between">
<div>
<h2 className="font-semibold">Retention operations</h2>
<p className="mt-1 text-xs text-slate-400">Daily bounded cleanup; legal-held organizations are excluded.</p>
</div>
<Archive className="h-5 w-5 text-[#0b5cff]"/>
</div>
<div className="mt-5 grid gap-3 sm:grid-cols-4">{[['Analytics',operations.retention?.analyticsRetentionDays],['Audit',operations.retention?.auditRetentionDays],['Notifications',operations.retention?.notificationRetentionDays],['Export access',operations.retention?.exportExpiryDays]].map(([label,days])=>
<div key={label as string} className="rounded-xl bg-slate-50 p-4 dark:bg-white/[.035]">
<strong className="text-lg">{days as number}</strong>
<p className="mt-1 text-[10px] text-slate-400">{label as string} days</p>
</div>)}</div>
<div className="mt-5 space-y-2">{operations.retentionRuns?.slice(0,5).map(run=>
<div key={run.id} className="flex items-center justify-between rounded-lg border border-slate-100 px-4 py-3 text-xs dark:border-white/5">
<span>
<strong>{run.status}</strong> · {run.completedAt?new Date(run.completedAt).toLocaleString():'running'}</span>
<span className="text-slate-400">{(run.usageEventsDeleted??0)+(run.usageRollupsDeleted??0)+(run.notificationsDeleted??0)+(run.storedFileRowsDeleted??0)+(run.exportRowsDeleted??0)+(run.lifecycleRowsDeleted??0)+(run.auditRowsDeleted??0)} rows removed</span>
</div>)}
{!operations.retentionRuns?.length&&<p className="text-xs text-slate-400">No cleanup run has completed yet. The first run is scheduled by Background Jobs.</p>}</div>
</Panel>}
    {!!operations?.activeSupportAccess?.length&&<Panel className="p-6">
<h2 className="font-semibold">Approved support access</h2>
<p className="mt-1 text-xs text-slate-400">Read-only, expiring sessions with explicit operator approval.</p>
<div className="mt-4 grid gap-3 md:grid-cols-2">{operations.activeSupportAccess.map(grant=>
<div key={grant.id} className="flex items-center gap-3 rounded-xl bg-slate-50 p-4 dark:bg-white/[.035]">
<ShieldCheck className="h-4 w-4 text-[#0b5cff]"/>
<div className="min-w-0 flex-1">
<p className="truncate text-xs font-semibold">{customers.find(x=>x.workspaceId===grant.workspaceId)?.name??grant.workspaceId}</p>
<p className="truncate text-[10px] text-slate-400">Until {grant.expiresAt?new Date(grant.expiresAt).toLocaleString():'—'} · {grant.reason}</p>
</div>{operations.capabilities?.canApproveSupportAccess?<button onClick={()=>grant.id&&void operation(grant.id,()=>client.api(new RevokeSupportAccessGrant({id:grant.id})),'Support access revoked.')} className="text-xs font-semibold text-red-600">Revoke</button>:grant.accessStartedAt?<button onClick={()=>grant.id&&void operation(grant.id,()=>client.api(new EndSupportAccess({id:grant.id})),'Support session ended.')} className="text-xs font-semibold text-red-600">End</button>:<button onClick={()=>grant.id&&void operation(grant.id,()=>client.api(new StartSupportAccess({id:grant.id})),'Read-only support session started.').then(()=>loadCustomer(grant.workspaceId!))} className="text-xs font-semibold text-[#0b5cff]">Start</button>}</div>)}</div>
</Panel>}
    {operations?.capabilities?.canManagePlatform&&<Panel className="overflow-hidden">
<div className="flex flex-col gap-3 border-b border-slate-200 px-6 py-5 sm:flex-row sm:items-center sm:justify-between dark:border-white/10">
<div>
<h2 className="font-semibold">Platform audit stream</h2>
<p className="mt-1 text-xs text-slate-400">Search actors, targets, reasons, and request correlation IDs.</p>
</div>
<div className="flex gap-2">
<form onSubmit={async e=>{e.preventDefault();const api=await client.api(new QueryPlatformAuditEvents({search:auditSearch||undefined,take:100}));if(api.succeeded)setAudits(api.response?.results??[])}} className="relative">
<Search className="absolute left-3 top-2.5 h-4 w-4 text-slate-400"/>
<input value={auditSearch} onChange={e=>setAuditSearch(e.target.value)} placeholder="Search audit" className="w-52 rounded-[10px] border-slate-200 py-2 pl-9 pr-3 text-sm dark:border-white/10 dark:bg-white/5"/>
</form>
<button onClick={async()=>{const blob=await client.get(new ExportPlatformAuditCsv({search:auditSearch||undefined,days:90}));const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download='platform-audit.csv';a.click();URL.revokeObjectURL(url)}} className="grid h-10 w-10 place-items-center rounded-[10px] border border-slate-200 dark:border-white/10" title="Export 90 days">
<Download className="h-4 w-4"/>
</button>
</div>
</div>
<div className="divide-y divide-slate-100 dark:divide-white/5">{audits.slice(0,50).map(x=>
<div key={x.id} className="grid gap-2 px-6 py-4 text-xs md:grid-cols-[1fr_1fr_auto]">
<div>
<strong>{x.action}</strong>
<p className="mt-1 text-slate-400">{x.category} · {x.workspaceId||'platform'}</p>
</div>
<div className="text-slate-500">
<p>{x.actorId}</p>
<p className="mt-1 truncate text-[10px]">{x.reason||x.subjectId}</p>
</div>
<span className="text-[10px] text-slate-400">{x.createdDate?new Date(x.createdDate).toLocaleString():'—'}</span>
</div>)}
{!audits.length&&<div className="p-8 text-center text-sm text-slate-400">No matching audit events.</div>}</div>
</Panel>}</>}
    {pending&&<div className="fixed inset-0 z-[80] grid place-items-center bg-[#07101f]/70 p-4 backdrop-blur-sm" role="dialog" aria-modal="true">
<Panel className="w-full max-w-lg overflow-hidden shadow-2xl">
<div className="flex items-start justify-between border-b border-slate-200 p-6 dark:border-white/10">
<div>
<p className="text-[10px] font-bold uppercase tracking-[.16em] text-[#0b5cff]">Review impact</p>
<h2 className="mt-2 text-xl font-semibold">{pending.preview.title}</h2>
</div>
<button onClick={()=>setPending(undefined)} className="grid h-9 w-9 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-white/5">
<X className="h-4 w-4"/>
</button>
</div>
<div className="space-y-5 p-6">
<div className="space-y-2">{pending.preview.impact?.map(x=>
<div key={x} className="flex gap-3 rounded-lg bg-slate-50 p-3 text-xs dark:bg-white/[.035]">
<CheckCircle2 className="h-4 w-4 shrink-0 text-[#0b5cff]"/>
<span>{x}</span>
</div>)}</div>{pending.preview.warnings?.map(x=>
<p key={x} className="rounded-lg bg-amber-50 p-3 text-xs text-amber-800 dark:bg-amber-400/10 dark:text-amber-200">{x}</p>)}<label className="block text-xs font-semibold">Business reason<textarea value={reason} onChange={e=>setReason(e.target.value)} rows={2} className="mt-2 block w-full rounded-[10px] border-slate-200 text-sm dark:border-white/10 dark:bg-white/5"/>
</label>
<label className="block text-xs font-semibold">Type <strong>{pending.preview.confirmation}</strong> to confirm<input value={confirmation} onChange={e=>setConfirmation(e.target.value)} className="mt-2 block w-full rounded-[10px] border-slate-200 text-sm dark:border-white/10 dark:bg-white/5"/>
</label>
<button disabled={!reason.trim()||confirmation!==pending.preview.confirmation||!!busy} onClick={async()=>{const current=pending;setPending(undefined);await current.run(confirmation,reason)}} className="w-full rounded-[10px] bg-[#0b5cff] px-4 py-3 text-sm font-semibold text-white disabled:opacity-40">Confirm operation</button>
</div>
</Panel>
</div>}
  </div>
}
