'use client'

import Link from 'next/link'
import { FormEvent, useEffect, useRef, useState } from 'react'
import { Activity, ArrowUpRight, CreditCard, Database, Gauge, Layers3, Settings2, ShieldCheck, TicketPercent, Users, Webhook } from 'lucide-react'
import AppShell, { PageHeading, Panel, StatusPill } from '@/components/app-shell'
import { PlanEditor } from '@/components/plan-editor'
import { CouponManager } from '@/components/coupon-manager'
import SaasOperations from '@/components/saas-operations'
import { appAuth } from '@/lib/auth'
import { GetSaasAdmin, GetSaasAdminResponse, SaasPlan, SaveCustomerOverride } from '@/lib/dtos'
import { client } from '@/lib/gateway'
import { product } from '@/lib/product'

export type AdminSection = 'overview' | 'customers' | 'plans' | 'usage' | 'operations' | 'security' | 'settings'

const headings: Record<AdminSection,{title:string;description:string}> = {
  overview: { title:'Operations overview', description:'Monitor the commercial and operational health of the SaaS platform.' },
  customers: { title:'Customers', description:'Find an organization, understand its effective state, and resolve account issues.' },
  plans: { title:'Plans & billing', description:'Manage plan versions, prices, trials, coupons, and the Stripe catalog.' },
  usage: { title:'Usage & analytics', description:'Review growth, plan adoption, quota pressure, and metered product activity.' },
  operations: { title:'Operations', description:'Resolve asynchronous failures and inspect platform integration readiness.' },
  security: { title:'Security & data', description:'Review support access, retention activity, legal controls, and the platform audit trail.' },
  settings: { title:'Platform settings', description:'Understand which settings are deployed globally, stored with plans, or applied per customer.' },
}

const roleCards = [
  {href:'/admin/customers',title:'Customer operations',body:'Search organizations and diagnose billing, access, quotas, and support context.',icon:Users,roles:['Support','BillingAdmin']},
  {href:'/admin/operations',title:'Operational queue',body:'Review failed deliveries, Stripe events, lifecycle work, and integration health.',icon:Activity,roles:['BillingAdmin']},
  {href:'/admin/security',title:'Security & data',body:'Manage approved support access and review retention and audit controls.',icon:ShieldCheck,roles:['Support']},
]

export default function AdminCenterPage({section}:{section:AdminSection}) {
  const {hasRole}=appAuth()
  const isAdmin=hasRole('Admin')
  const [data,setData]=useState<GetSaasAdminResponse>()
  const [loading,setLoading]=useState(isAdmin&&['overview','customers','plans'].includes(section))
  const [error,setError]=useState<string>()
  const [overrideMessage,setOverrideMessage]=useState<{ok:boolean;text:string}>()
  const [savingOverride,setSavingOverride]=useState(false)
  const [billingTab,setBillingTab]=useState<'plans'|'coupons'>('plans')
  const loaded=useRef(false)

  useEffect(()=>{
    if(loaded.current||!isAdmin||!['overview','customers','plans'].includes(section)){setLoading(false);return}
    loaded.current=true
    void client.api(new GetSaasAdmin()).then(api=>{
      if(api.succeeded&&api.response)setData(api.response)
      else setError(api.error?.message??'Unable to load SaaS administration data.')
      setLoading(false)
    })
  },[isAdmin,section])

  const updatePlan=(updated:SaasPlan)=>setData(current=>current?{...current,plans:(current.plans??[]).map(plan=>plan.id===updated.id?updated:plan).sort((a,b)=>(a.displayOrder??0)-(b.displayOrder??0))}:current)
  const saveOverride=async(event:FormEvent<HTMLFormElement>)=>{
    event.preventDefault();const formElement=event.currentTarget;const form=new FormData(formElement);const validUntil=String(form.get('validUntil')??'')
    setSavingOverride(true);setOverrideMessage(undefined)
    const api=await client.api(new SaveCustomerOverride({workspaceId:String(form.get('workspaceId')),key:String(form.get('key')),quotaUnits:Number(form.get('quotaUnits')),validUntil:validUntil?new Date(`${validUntil}T23:59:59Z`).toISOString():undefined,reason:String(form.get('reason'))}))
    setOverrideMessage(api.succeeded?{ok:true,text:'Customer override saved and added to the audit trail.'}:{ok:false,text:api.error?.message??'Unable to save this override.'})
    setSavingOverride(false);if(api.succeeded)formElement.reset()
  }

  const heading=headings[section]
  const metrics=[
    {icon:Layers3,label:'Published plans',value:data?.versions?.filter(x=>x.status==='Published').length??0,tone:'blue' as const},
    {icon:Database,label:'Customer organizations',value:data?.workspaces?.length??0,tone:'green' as const},
    {icon:Webhook,label:'Recent Stripe events',value:data?.recentStripeEvents?.length??0,tone:'slate' as const},
    {icon:Activity,label:'Failed events',value:data?.recentStripeEvents?.filter(x=>x.status==='Failed').length??0,tone:'amber' as const},
  ]

  return <AppShell>
    <PageHeading eyebrow={`${product.name} operations center`} title={heading.title} description={heading.description} action={isAdmin?<a href="/admin-ui" className="inline-flex items-center gap-2 rounded-[10px] border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 shadow-sm transition hover:border-slate-400 dark:border-white/15 dark:bg-white/5 dark:text-white">ServiceStack Admin <ArrowUpRight className="h-4 w-4"/></a>:undefined}/>
    {error&&<Panel className="mb-5 border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-400/20 dark:bg-red-400/10 dark:text-red-200">{error}</Panel>}

    {section==='overview'&&<>
      {isAdmin&&<div className="mb-5 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">{metrics.map(({icon:Icon,label,value,tone})=><Panel key={label} className="p-5"><div className="flex items-center justify-between"><span className="grid h-9 w-9 place-items-center rounded-lg bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><Icon className="h-4 w-4"/></span>{tone==='amber'&&value>0&&<StatusPill tone="amber">Needs review</StatusPill>}</div><p className="tabular mt-5 text-2xl font-semibold tracking-[-.04em]">{value}</p><p className="mt-1 text-xs text-slate-400">{label}</p></Panel>)}</div>}
      {!isAdmin&&<div className="mb-5 grid gap-5 lg:grid-cols-3">{roleCards.filter(x=>x.roles.some(hasRole)).map(({href,title,body,icon:Icon})=><Link key={href} href={href}><Panel className="h-full p-6 transition hover:-translate-y-0.5 hover:border-blue-300 hover:shadow-md"><span className="grid h-10 w-10 place-items-center rounded-[10px] bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><Icon className="h-5 w-5"/></span><h2 className="mt-5 font-semibold">{title}</h2><p className="mt-2 text-xs leading-5 text-slate-500 dark:text-slate-400">{body}</p></Panel></Link>)}</div>}
      <SaasOperations view="overview"/>
    </>}

    {section==='customers'&&<><SaasOperations view="customers"/>{isAdmin&&<Panel className="p-6"><div className="flex items-start gap-3"><span className="grid h-10 w-10 shrink-0 place-items-center rounded-[10px] bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><Gauge className="h-5 w-5"/></span><div><h2 className="font-semibold">Customer quota override</h2><p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">Grant an organization a contracted allowance without changing its plan for everyone else.</p></div></div><form onSubmit={saveOverride} className="mt-6 grid gap-4 sm:grid-cols-2"><label className="block text-xs font-semibold">Organization<select name="workspaceId" required className="mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-[#0c1729]"><option value="">Choose organization</option>{data?.workspaces?.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label className="block text-xs font-semibold">Meter key<input name="key" required defaultValue="documents.stored" className="mt-2 block w-full rounded-[10px] border-slate-200 bg-white font-mono text-sm dark:border-white/10 dark:bg-[#0c1729]"/></label><label className="block text-xs font-semibold">Allowance<input name="quotaUnits" required type="number" min="0" placeholder="100000" className="mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-[#0c1729]"/></label><label className="block text-xs font-semibold">Valid until <span className="font-normal text-slate-400">(optional)</span><input name="validUntil" type="date" className="mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-[#0c1729]"/></label><label className="block text-xs font-semibold sm:col-span-2">Business reason<textarea name="reason" required rows={3} className="mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm dark:border-white/10 dark:bg-[#0c1729]"/></label><div className="flex flex-col gap-3 sm:col-span-2 sm:flex-row sm:items-center sm:justify-between">{overrideMessage?<p className={`text-xs ${overrideMessage.ok?'text-emerald-600':'text-red-600'}`}>{overrideMessage.text}</p>:<p className="text-[11px] text-slate-400">Overrides are actor-stamped and stored in the RDBMS.</p>}<button disabled={savingOverride} className="rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-50">{savingOverride?'Saving…':'Save override'}</button></div></form></Panel>}</>}

    {section==='plans'&&<>
      <div className="mb-5 flex w-full gap-1 rounded-[12px] border border-slate-200 bg-slate-100/80 p-1 dark:border-white/10 dark:bg-white/[.035] sm:w-fit" role="tablist" aria-label="Plans and billing management">
        <button type="button" role="tab" aria-selected={billingTab==='plans'} onClick={()=>setBillingTab('plans')} className={`flex flex-1 items-center justify-center gap-2 rounded-[9px] px-5 py-2.5 text-sm font-semibold transition sm:flex-none ${billingTab==='plans'?'bg-white text-slate-950 shadow-sm dark:bg-white/10 dark:text-white':'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-white'}`}><Layers3 className="h-4 w-4"/>Plans</button>
        <button type="button" role="tab" aria-selected={billingTab==='coupons'} onClick={()=>setBillingTab('coupons')} className={`flex flex-1 items-center justify-center gap-2 rounded-[9px] px-5 py-2.5 text-sm font-semibold transition sm:flex-none ${billingTab==='coupons'?'bg-white text-slate-950 shadow-sm dark:bg-white/10 dark:text-white':'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-white'}`}><TicketPercent className="h-4 w-4"/>Coupons</button>
      </div>
      {billingTab==='plans'&&(loading?<Loading label="Loading plan catalog"/>:data?.plans?.length?<PlanEditor plans={data.plans} versions={data.versions??[]} trialsEnabled={data.trialsEnabled??true} trialRequiresPaymentMethod={data.trialRequiresPaymentMethod??false} defaultTrialDays={data.defaultTrialDays??14} stripeConfigured={data.stripeConfigured??false} stripeCatalogProvisioningEnabled={data.stripeCatalogProvisioningEnabled??false} stripeMode={data.stripeMode??'Not configured'} onPlanUpdated={updatePlan}/>:<EmptyPlans/>)}
      {billingTab==='coupons'&&<CouponManager/>}
    </>}
    {section==='usage'&&<SaasOperations view="usage"/>}
    {section==='operations'&&<SaasOperations view="operations"/>}
    {section==='security'&&<SaasOperations view="security"/>}
    {section==='settings'&&<SettingsOverview/>}
  </AppShell>
}

function Loading({label}:{label:string}){return <Panel className="grid min-h-72 place-items-center"><div className="text-center"><span className="mx-auto block h-8 w-8 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]"/><p className="mt-3 text-xs text-slate-400">{label}</p></div></Panel>}
function EmptyPlans(){return <Panel className="grid min-h-64 place-items-center p-8 text-center"><div><Layers3 className="mx-auto h-6 w-6 text-slate-300"/><h2 className="mt-3 font-semibold">No plans configured</h2><p className="mt-2 text-sm text-slate-500">Create the initial plan records in ServiceStack Admin.</p></div></Panel>}
function SettingsOverview(){return <div className="grid gap-5 xl:grid-cols-3">{[
  {icon:Settings2,title:'Global policy',body:'Deployment-wide defaults, feature switches, retention schedules, and provider settings remain reviewable in appsettings.json.',tone:'slate'},
  {icon:CreditCard,title:'Plan policy',body:'Catalog details, quotas, trials, prices, and immutable commercial versions live in the RDBMS.',tone:'blue'},
  {icon:ShieldCheck,title:'Customer policy',body:'Time-bound customer exceptions have higher priority and retain their actor and business reason.',tone:'green'},
].map(({icon:Icon,title,body,tone})=><Panel key={title} className="p-6"><span className="grid h-10 w-10 place-items-center rounded-[10px] bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><Icon className="h-5 w-5"/></span><div className="mt-5 flex items-center justify-between"><h2 className="font-semibold">{title}</h2><StatusPill tone={tone as 'slate'|'blue'|'green'}>{title==='Global policy'?'JSON':'RDBMS'}</StatusPill></div><p className="mt-3 text-xs leading-5 text-slate-500 dark:text-slate-400">{body}</p></Panel>)}</div>}
