'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import Link from 'next/link'
import { ArrowRight, Check, ShieldCheck } from 'lucide-react'
import Layout from '@/components/layout'
import { appAuth } from '@/lib/auth'
import { client } from '@/lib/gateway'
import { BillingInterval, CreateCheckoutSession, GetMyWorkspaces, GetSaasPlans, PlanAudience, PlanInfo, QuotaEnforcement, WorkspaceKind } from '@/lib/dtos'
import { product } from '@/lib/product'

type SeedPlan = {
  Code: string
  Name: string
  Description: string
  Audience?: 'Both' | 'Individual' | 'Business'
  ContactSales?: boolean
  TrialDays?: number
  Monthly: number
  Annual: number
  Documents?: number
  StorageBytes?: number
  ApiRequests?: number
  Features: { Key: string; Name: string }[]
}

const fallback: PlanInfo[] = (JSON.parse(process.env.seedPlans ?? '[]') as SeedPlan[]).map(plan =>
  new PlanInfo({
    code: plan.Code,
    name: plan.Name,
    description: plan.Description,
    audience: (plan.Audience ?? 'Both') as PlanAudience,
    isContactSales: plan.ContactSales,
    trialDays: plan.TrialDays,
    features: plan.Features.map(x => x.Name),
    prices: plan.ContactSales ? [] : [
      { id: `price.${plan.Code}.month`, currency: 'usd', interval: BillingInterval.Month, unitAmount: plan.Monthly, checkoutReady: false },
      { id: `price.${plan.Code}.year`, currency: 'usd', interval: BillingInterval.Year, unitAmount: plan.Annual, checkoutReady: false },
    ],
    quotas: [
      { meterKey: 'documents.stored', displayName: 'Documents stored', includedUnits: plan.Documents, enforcement: QuotaEnforcement.HardLimit, rolloverEnabled: false },
      { meterKey: 'storage.bytes', displayName: 'Storage', includedUnits: plan.StorageBytes, enforcement: QuotaEnforcement.HardLimit, rolloverEnabled: false },
      { meterKey: 'api.requests', displayName: 'API requests', includedUnits: plan.ApiRequests, enforcement: QuotaEnforcement.HardLimit, rolloverEnabled: false },
    ],
  }))

type CatalogState = 'loading' | 'ready' | 'error'

export default function PricingPage() {
  const { user } = appAuth()
  const [catalogPlans,setPlans] = useState(fallback)
  const [audience,setAudience] = useState<WorkspaceKind>(WorkspaceKind.Individual)
  const [activeKind,setActiveKind] = useState<WorkspaceKind>()
  const plans = catalogPlans.filter(plan => !plan.audience || plan.audience === PlanAudience.Both || plan.audience === (audience === WorkspaceKind.Individual ? PlanAudience.Individual : PlanAudience.Business))
  const [catalogState,setCatalogState] = useState<CatalogState>('loading')
  const [annual,setAnnual] = useState(false)
  const [busy,setBusy] = useState<string>()
  const [message,setMessage] = useState<string>()
  const initialLoadStarted = useRef(false)

  const loadCatalog = useCallback(async () => {
    setCatalogState('loading')
    try {
      const api = await client.api(new GetSaasPlans())
      if (api.succeeded && api.response?.results) {
        setPlans(api.response.results)
        setCatalogState('ready')
        return true
      }
    } catch {
      // The fallback catalog remains useful for marketing copy, but is never checkout data.
    }
    setCatalogState('error')
    return false
  }, [])

  useEffect(() => {
    if (initialLoadStarted.current) return
    initialLoadStarted.current = true
    void loadCatalog()
  }, [loadCatalog])

  useEffect(() => {
    if (!user?.userId) return
    void client.api(new GetMyWorkspaces()).then(api => {
      const kind = api.response?.results?.find(x => x.isActive)?.workspace?.kind
      if (api.succeeded && kind) { setAudience(kind); setActiveKind(kind) }
    })
  }, [user?.userId])

  const checkout = async (plan:PlanInfo) => {
    const price = plan.prices?.find(x => x.interval === (annual ? BillingInterval.Year : BillingInterval.Month))
    if (!user) { location.href = `/signup?account=${audience === WorkspaceKind.Business ? 'business' : 'individual'}&redirect=${encodeURIComponent('/pricing')}`; return }
    if (activeKind && activeKind !== audience) { setMessage('Switch to a matching account in the application before choosing this plan. You can create a business organization from Settings.'); return }
    if (!price || price.unitAmount === 0) { location.href='/dashboard'; return }
    if (catalogState === 'loading') { setMessage('Live pricing is still loading. Please wait before starting checkout.'); return }
    if (catalogState === 'error') { setMessage('Checkout is temporarily unavailable because live pricing could not be loaded. Please try again.'); return }
    if (!price.checkoutReady) { setMessage('This price is not available for checkout. Please choose another option or try again later.'); return }
    setBusy(plan.code); setMessage(undefined)
    const api = await client.api(new CreateCheckoutSession({priceId:price.id}))
    if (api.succeeded && api.response?.url) location.href=api.response.url
    else if (api.error?.errorCode === 'PlanNotAvailable') {
      const refreshed = await loadCatalog()
      setMessage(refreshed
        ? 'Pricing changed while this page was open. Review the latest plans and retry checkout.'
        : 'Pricing changed, but the latest catalog could not be loaded. Please try again later.')
    } else setMessage(api.error?.message || 'Checkout is not configured yet.')
    setBusy(undefined)
  }
  return <Layout>
    <section className="relative overflow-hidden bg-white pb-16 pt-20 dark:bg-[#07101f] lg:pb-20 lg:pt-28"><div className="enterprise-grid absolute inset-0"/><div className="relative mx-auto max-w-7xl px-5 text-center lg:px-8"><p className="text-xs font-bold uppercase tracking-[.2em] text-[#0b5cff] dark:text-[#86efcd]">Simple, usage-aware pricing</p><h1 className="mx-auto mt-5 max-w-3xl text-5xl font-semibold tracking-[-.055em] text-slate-950 sm:text-6xl dark:text-white">Start with secure storage. Scale with confidence.</h1><p className="mx-auto mt-6 max-w-2xl text-lg leading-8 text-slate-600 dark:text-slate-300">Every plan includes tenant-isolated document storage, transparent quotas, and useful usage analytics.</p></div></section>
    <section className="bg-[#f6f8fb] pb-24 pt-10 dark:bg-[#0a1424]"><div className="mx-auto mb-8 flex max-w-7xl flex-col items-center gap-4 px-5 sm:flex-row sm:justify-between lg:px-8"><div role="group" aria-label="Plan type" className="flex w-full gap-1 rounded-xl border border-slate-200 bg-slate-100 p-1 dark:border-white/10 dark:bg-white/[.035] sm:w-auto"><button type="button" aria-pressed={audience === WorkspaceKind.Individual} onClick={() => setAudience(WorkspaceKind.Individual)} className={`flex-1 rounded-lg px-5 py-2.5 text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#0b5cff] sm:flex-none ${audience === WorkspaceKind.Individual ? 'bg-white text-slate-950 shadow-sm dark:bg-white/10 dark:text-white' : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-white'}`}>Personal</button><button type="button" aria-pressed={audience === WorkspaceKind.Business} onClick={() => setAudience(WorkspaceKind.Business)} className={`flex-1 rounded-lg px-5 py-2.5 text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#0b5cff] sm:flex-none ${audience === WorkspaceKind.Business ? 'bg-white text-slate-950 shadow-sm dark:bg-white/10 dark:text-white' : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-white'}`}>Business</button></div><div role="group" aria-label="Billing interval" className="flex w-full gap-1 rounded-xl border border-slate-200 bg-slate-100 p-1 dark:border-white/10 dark:bg-white/[.035] sm:w-auto"><button type="button" aria-pressed={!annual} onClick={() => setAnnual(false)} className={`flex-1 rounded-lg px-4 py-2.5 text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#0b5cff] sm:flex-none ${!annual ? 'bg-white text-slate-950 shadow-sm dark:bg-white/10 dark:text-white' : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-white'}`}>Monthly</button><button type="button" aria-pressed={annual} onClick={() => setAnnual(true)} className={`flex-1 rounded-lg px-4 py-2.5 text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#0b5cff] sm:flex-none ${annual ? 'bg-white text-slate-950 shadow-sm dark:bg-white/10 dark:text-white' : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-white'}`}>Annual <span className="ml-1 text-emerald-600 dark:text-[#86efcd]">2 months free</span></button></div></div><div className="mx-auto grid max-w-7xl gap-5 px-5 lg:grid-cols-4 lg:px-8">{plans.map(plan => { const featured=plan.code==='business'; const price=plan.prices?.find(x=>x.interval===(annual?BillingInterval.Year:BillingInterval.Month)); const monthly=annual ? (price?.unitAmount||0)/1200 : (price?.unitAmount||0)/100; const documents=plan.quotas?.find(x=>x.meterKey==='documents.stored')?.includedUnits; const storage=plan.quotas?.find(x=>x.meterKey==='storage.bytes')?.includedUnits; const api=plan.quotas?.find(x=>x.meterKey==='api.requests')?.includedUnits; const paid=(price?.unitAmount??0)>0; const checkoutDisabled=!!user && !plan.isContactSales && paid && (catalogState!=='ready'||!price?.checkoutReady); return <div key={plan.code} className={`relative flex flex-col rounded-[16px] border bg-white p-6 shadow-[0_12px_35px_rgba(16,24,40,.06)] dark:bg-[#0c1729] ${featured?'border-[#0b5cff] ring-1 ring-[#0b5cff] dark:border-blue-400':'border-slate-200 dark:border-white/10'}`}>{featured&&<span className="absolute -top-3 left-6 rounded-full bg-[#0b5cff] px-3 py-1 text-[10px] font-bold uppercase tracking-[.12em] text-white">Most popular</span>}<p className="text-lg font-semibold tracking-[-.02em] text-slate-950 dark:text-white">{plan.name}</p><p className="mt-2 min-h-12 text-sm leading-6 text-slate-500 dark:text-slate-400">{plan.description}</p><div className="mt-7 min-h-14">{plan.isContactSales?<p className="text-3xl font-semibold tracking-[-.04em]">Let’s talk</p>:<p><span className="text-4xl font-semibold tracking-[-.05em] text-slate-950 dark:text-white">${Math.round(monthly)}</span><span className="text-sm text-slate-400"> / month</span></p>}</div><button onClick={()=>plan.isContactSales?location.href=`mailto:${product.salesEmail}`:checkout(plan)} disabled={busy===plan.code||checkoutDisabled} title={checkoutDisabled ? catalogState==='loading' ? 'Live pricing is still loading.' : catalogState==='error' ? 'Checkout is temporarily unavailable.' : 'This price is not available for checkout.' : undefined} className={`mt-6 inline-flex items-center justify-center gap-2 rounded-[10px] px-4 py-3 text-sm font-semibold transition hover:-translate-y-px disabled:cursor-not-allowed disabled:opacity-60 ${featured?'bg-[#0b5cff] text-white shadow-[0_8px_24px_rgba(11,92,255,.22)]':'border border-slate-300 text-slate-800 hover:border-slate-400 dark:border-white/15 dark:text-white'}`}>{busy===plan.code?'Opening checkout…':plan.isContactSales?'Contact sales':plan.code==='free'?'Start free':`Try ${plan.name}`}<ArrowRight className="h-4 w-4"/></button><div className="my-6 border-t border-slate-200 dark:border-white/10"/><div className="space-y-2 text-xs font-semibold text-slate-800 dark:text-slate-100"><p>{documents?`${documents.toLocaleString()} documents`:'Custom document volume'}</p><p>{storage?`${storage>=1024**3?`${Math.round(storage/1024**3)} GB`:`${Math.round(storage/1024**2)} MB`} storage`:'Custom storage'}</p><p>{api?`${api.toLocaleString()} API requests / month`:'Custom API usage'}</p></div><ul className="mt-5 space-y-3">{plan.features?.map(x=><li key={x} className="flex gap-2.5 text-sm text-slate-600 dark:text-slate-300"><Check className="mt-0.5 h-4 w-4 shrink-0 text-emerald-500"/>{x}</li>)}</ul></div>})}</div>{user&&catalogState==='loading'&&<div className="mx-auto mt-6 max-w-2xl rounded-xl border border-blue-200 bg-blue-50 p-4 text-center text-sm text-blue-800 dark:border-blue-400/20 dark:bg-blue-400/10 dark:text-blue-200">Live pricing is still loading.</div>}{user&&catalogState==='error'&&<div className="mx-auto mt-6 max-w-2xl rounded-xl border border-amber-200 bg-amber-50 p-4 text-center text-sm text-amber-800 dark:border-amber-400/20 dark:bg-amber-400/10 dark:text-amber-200">Checkout is temporarily unavailable because live pricing could not be loaded. <button type="button" onClick={()=>void loadCatalog()} className="font-semibold underline underline-offset-2">Try again</button>.</div>}{message&&<div className="mx-auto mt-6 max-w-2xl rounded-xl border border-amber-200 bg-amber-50 p-4 text-center text-sm text-amber-800 dark:border-amber-400/20 dark:bg-amber-400/10 dark:text-amber-200">{message}</div>}<div className="mx-auto mt-12 flex max-w-4xl items-center justify-center gap-3 text-center text-xs text-slate-500"><ShieldCheck className="h-4 w-4 text-emerald-500"/>Secure Stripe checkout · Hard usage limits by default · Cancel any time</div></section>
    <section className="bg-white py-20 dark:bg-[#07101f]"><div className="mx-auto max-w-4xl px-5 text-center"><h2 className="text-3xl font-semibold tracking-[-.04em]">Questions before you choose?</h2><p className="mt-4 text-slate-500 dark:text-slate-400">Start on Free with no payment method, or talk to us about migration, data residency, security, and custom capacity.</p><div className="mt-7 flex justify-center gap-3"><Link href={`/signup?account=${audience === WorkspaceKind.Business ? 'business' : 'individual'}`} className="rounded-[10px] bg-[#0b5cff] px-5 py-3 text-sm font-semibold text-white">{audience === WorkspaceKind.Business ? 'Create an organization' : 'Create an account'}</Link><a href={`mailto:${product.salesEmail}`} className="rounded-[10px] border border-slate-300 px-5 py-3 text-sm font-semibold dark:border-white/15">Talk to sales</a></div></div></section>
  </Layout>
}
