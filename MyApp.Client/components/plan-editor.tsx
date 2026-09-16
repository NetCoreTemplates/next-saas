'use client'

import { FormEvent, useEffect, useRef, useState } from 'react'
import {
  Archive,
  BadgeDollarSign,
  Check,
  ChevronRight,
  CircleGauge,
  Cloud,
  Eye,
  EyeOff,
  FileClock,
  GripVertical,
  ListChecks,
  Plus,
  Rocket,
  Save,
  Sparkles,
  Trash2,
  Users,
} from 'lucide-react'
import { client } from '@/lib/gateway'
import {
  BillingInterval,
  GetSaasPlanDetails,
  PlanVersionStatus,
  ProvisionSaasPlanStripeCatalog,
  PublishSaasPlanDraft,
  QuotaEnforcement,
  SaasPlan,
  SaasPlanDetails,
  SaasPlanVersion,
  SavePlanFeature,
  SavePlanPrice,
  SavePlanQuota,
  SaveSaasPlanDraft,
} from '@/lib/dtos'
import { Panel, StatusPill } from '@/components/app-shell'

type EditorTab = 'catalog' | 'pricing' | 'features' | 'quotas'
type PriceRow = { id: string; currency: string; interval: BillingInterval; unitAmount: string; stripePriceId: string; isActive: boolean }
type FeatureRow = { id: string; key: string; name: string; description: string; enabled: boolean }
type QuotaRow = { id: string; meterKey: string; displayName: string; includedUnits: string; enforcement: QuotaEnforcement; rolloverEnabled: boolean }
type EditorState = {
  name: string
  description: string
  displayOrder: string
  trialEnabled: boolean
  trialDays: string
  isPublic: boolean
  isContactSales: boolean
  isArchived: boolean
  prices: PriceRow[]
  features: FeatureRow[]
  quotas: QuotaRow[]
}

const fieldClass = 'mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm shadow-sm transition focus:border-[#0b5cff] focus:ring-[#0b5cff]/20 dark:border-white/10 dark:bg-[#0c1729]'
const labelClass = 'block text-xs font-semibold text-slate-600 dark:text-slate-300'
const newId = () => typeof crypto !== 'undefined' ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`
const loadPlanDetails = (planId: string) => client.api(new GetSaasPlanDetails({ planId }))

function toEditor(details: SaasPlanDetails, defaultTrialDays: number): EditorState {
  return {
    name: details.plan?.name ?? '',
    description: details.plan?.description ?? '',
    displayOrder: String(details.plan?.displayOrder ?? 0),
    trialEnabled: (details.version?.trialDays ?? 0) > 0,
    trialDays: details.version?.trialDays == null ? String(defaultTrialDays) : String(details.version.trialDays),
    isPublic: details.plan?.isPublic ?? true,
    isContactSales: details.plan?.isContactSales ?? false,
    isArchived: details.plan?.isArchived ?? false,
    prices: (details.prices ?? []).map(x => ({
      id: x.id ?? newId(), currency: x.currency ?? 'usd', interval: x.interval ?? BillingInterval.Month,
      unitAmount: String(x.unitAmount ?? 0), stripePriceId: x.stripePriceId ?? '', isActive: x.isActive ?? true,
    })),
    features: (details.features ?? []).map(x => ({
      id: x.id ?? newId(), key: x.key ?? '', name: x.name ?? '', description: x.description ?? '', enabled: x.enabled ?? true,
    })),
    quotas: (details.quotas ?? []).map(x => ({
      id: x.id ?? newId(), meterKey: x.meterKey ?? '', displayName: x.displayName ?? '',
      includedUnits: x.includedUnits == null ? '' : String(x.includedUnits),
      enforcement: x.enforcement ?? QuotaEnforcement.HardLimit, rolloverEnabled: x.rolloverEnabled ?? false,
    })),
  }
}

function Toggle({ checked, onChange, label, description, icon: Icon, disabled = false }: {
  checked: boolean; onChange: (value: boolean) => void; label: string; description: string; icon: typeof Eye; disabled?: boolean
}) {
  return <button type="button" disabled={disabled} onClick={() => onChange(!checked)} className={`flex w-full items-center gap-4 rounded-xl border p-4 text-left transition disabled:cursor-not-allowed disabled:opacity-60 ${checked ? 'border-blue-200 bg-blue-50/70 dark:border-blue-400/20 dark:bg-blue-400/[.08]' : 'border-slate-200 bg-slate-50/70 dark:border-white/10 dark:bg-white/[.025]'}`}>
    <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-[10px] ${checked ? 'bg-[#0b5cff] text-white' : 'bg-white text-slate-400 shadow-sm dark:bg-white/5'}`}><Icon className="h-[18px] w-[18px]"/></span>
    <span className="min-w-0 flex-1"><span className="block text-sm font-semibold">{label}</span><span className="mt-1 block text-xs leading-5 text-slate-500 dark:text-slate-400">{description}</span></span>
    <span className={`relative h-6 w-11 rounded-full transition ${checked ? 'bg-[#0b5cff]' : 'bg-slate-300 dark:bg-slate-700'}`}><span className={`absolute top-1 h-4 w-4 rounded-full bg-white shadow-sm transition ${checked ? 'left-6' : 'left-1'}`}/></span>
  </button>
}

function EmptyState({ icon: Icon, title, body, action }: { icon: typeof Sparkles; title: string; body: string; action: React.ReactNode }) {
  return <div className="grid min-h-56 place-items-center rounded-xl border border-dashed border-slate-300 bg-slate-50/50 p-8 text-center dark:border-white/15 dark:bg-white/[.02]">
    <div><span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><Icon className="h-5 w-5"/></span><h3 className="mt-4 text-sm font-semibold">{title}</h3><p className="mx-auto mt-2 max-w-sm text-xs leading-5 text-slate-500 dark:text-slate-400">{body}</p><div className="mt-4">{action}</div></div>
  </div>
}

export function PlanEditor({ plans, versions, trialsEnabled, trialRequiresPaymentMethod, defaultTrialDays, stripeConfigured, stripeCatalogProvisioningEnabled, stripeMode, onPlanUpdated }: {
  plans: SaasPlan[]; versions: SaasPlanVersion[]; trialsEnabled: boolean; trialRequiresPaymentMethod: boolean; defaultTrialDays: number; stripeConfigured: boolean; stripeCatalogProvisioningEnabled: boolean; stripeMode: string; onPlanUpdated: (plan: SaasPlan) => void
}) {
  const [selectedId, setSelectedId] = useState(plans[0]?.id ?? '')
  const [catalogVersions, setCatalogVersions] = useState(versions)
  const [details, setDetails] = useState<SaasPlanDetails>()
  const [editor, setEditor] = useState<EditorState>()
  const [tab, setTab] = useState<EditorTab>('catalog')
  const [busy, setBusy] = useState<'load' | 'save' | 'publish' | 'stripe'>()
  const [notice, setNotice] = useState<{ tone: 'success' | 'error'; text: string }>()
  const [dirty, setDirty] = useState(false)
  const pendingLoad = useRef<{ planId: string; request: ReturnType<typeof loadPlanDetails> } | undefined>(undefined)

  useEffect(() => {
    if (!selectedId) return
    let cancelled = false
    setBusy('load')
    setNotice(undefined)

    const request = pendingLoad.current?.planId === selectedId
      ? pendingLoad.current.request
      : loadPlanDetails(selectedId)
    pendingLoad.current = { planId: selectedId, request }

    request.then(api => {
      if (!cancelled) {
        if (api.succeeded && api.response) {
          setDetails(api.response)
          setEditor(toEditor(api.response, defaultTrialDays))
          setDirty(false)
        } else {
          setNotice({ tone: 'error', text: api.error?.message ?? 'Unable to load this plan.' })
        }
        setBusy(undefined)
      }
    }).catch(() => {
      if (!cancelled) {
        setNotice({ tone: 'error', text: 'Unable to load this plan.' })
        setBusy(undefined)
      }
    }).finally(() => {
      if (pendingLoad.current?.request === request)
        pendingLoad.current = undefined
    })
    return () => { cancelled = true }
  }, [selectedId, defaultTrialDays])

  useEffect(() => {
    if (!dirty) return
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const update = (change: (current: EditorState) => EditorState) => {
    setEditor(current => current ? change(current) : current)
    setDirty(true)
    setNotice(undefined)
  }

  const save = async (event?: FormEvent) => {
    event?.preventDefault()
    if (!details?.plan?.id || !editor) return
    setBusy('save')
    setNotice(undefined)
    const api = await client.api(new SaveSaasPlanDraft({
      planId: details.plan.id,
      name: editor.name,
      description: editor.description,
      displayOrder: Number(editor.displayOrder),
      isPublic: editor.isPublic,
      isContactSales: editor.isContactSales,
      isArchived: editor.isArchived,
      trialDays: trialsEnabled && editor.trialEnabled ? Number(editor.trialDays) : undefined,
      prices: editor.prices.map(x => new SavePlanPrice({
        currency: x.currency, interval: x.interval, unitAmount: Number(x.unitAmount),
        stripePriceId: x.stripePriceId || undefined, isActive: x.isActive,
      })),
      features: editor.features.map((x, index) => new SavePlanFeature({
        key: x.key, name: x.name, description: x.description || undefined, enabled: x.enabled, displayOrder: index,
      })),
      quotas: editor.quotas.map(x => new SavePlanQuota({
        meterKey: x.meterKey, displayName: x.displayName,
        includedUnits: x.includedUnits === '' ? undefined : Number(x.includedUnits),
        enforcement: x.enforcement, rolloverEnabled: x.rolloverEnabled,
      })),
    }))
    if (api.succeeded && api.response) {
      setDetails(api.response)
      setEditor(toEditor(api.response, defaultTrialDays))
      setDirty(false)
      if (api.response.plan) onPlanUpdated(api.response.plan)
      if (api.response.version) {
        const savedVersion = api.response.version
        setCatalogVersions(current => [...current.filter(x => x.id !== savedVersion.id), savedVersion])
      }
      setNotice({ tone: 'success', text: `Draft v${api.response.version?.version} saved. Published customers are unchanged.` })
    } else {
      setNotice({ tone: 'error', text: api.error?.message ?? 'Unable to save this plan.' })
    }
    setBusy(undefined)
  }

  const publish = async () => {
    if (!details?.plan?.id || dirty || !details.hasDraft) return
    if (!window.confirm(`Publish version ${details.version?.version}? Existing subscriptions will stay on their current version.`)) return
    setBusy('publish')
    setNotice(undefined)
    const api = await client.api(new PublishSaasPlanDraft({ planId: details.plan.id }))
    if (api.succeeded && api.response) {
      setDetails(api.response)
      setEditor(toEditor(api.response, defaultTrialDays))
      if (api.response.version) {
        const published = api.response.version
        setCatalogVersions(current => [
          ...current.filter(x => x.id !== published.id).map(x => x.planId === published.planId && x.status === PlanVersionStatus.Published
            ? new SaasPlanVersion({ ...x, status: PlanVersionStatus.Retired })
            : x),
          published,
        ])
      }
      setNotice({ tone: 'success', text: `Version ${api.response.version?.version} is now live for new subscriptions.` })
    } else {
      setNotice({ tone: 'error', text: api.error?.message ?? 'Unable to publish this plan.' })
    }
    setBusy(undefined)
  }

  const provisionStripe = async () => {
    if (!details?.plan?.id || !editor || dirty || !stripeCatalogProvisioningEnabled) return
    const missing = editor.prices.filter(x => x.isActive && Number(x.unitAmount) > 0 && !x.stripePriceId)
    if (missing.length === 0) {
      setNotice({ tone: 'success', text: 'Every positive active price is already mapped to Stripe.' })
      return
    }
    if (!window.confirm(`Create ${missing.length} missing recurring price${missing.length === 1 ? '' : 's'} in the configured Stripe ${stripeMode.toLowerCase()}? Stripe price amounts cannot be edited after creation.`)) return
    setBusy('stripe')
    setNotice(undefined)
    const api = await client.api(new ProvisionSaasPlanStripeCatalog({
      planId: details.plan.id,
    }))
    if (api.succeeded && api.response) {
      const mappings = api.response.prices ?? []
      setDetails(api.response.draft)
      setEditor(toEditor(api.response.draft, defaultTrialDays))
      setDirty(false)
      if (api.response.draft.version) {
        const savedVersion = api.response.draft.version
        setCatalogVersions(current => [...current.filter(x => x.id !== savedVersion.id), savedVersion])
      }
      const created = mappings.filter(x => x.created).length
      const reused = mappings.length - created
      setNotice({ tone: 'success', text: `${api.response.productCreated ? 'Created' : 'Reused'} Stripe product ${api.response.stripeProductId}; ${created} price${created === 1 ? '' : 's'} created${reused ? ` and ${reused} reused` : ''}. The mappings were saved to the draft; publish it to enable Checkout.` })
    } else {
      setNotice({ tone: 'error', text: api.error?.message ?? 'Unable to provision this plan in Stripe.' })
    }
    setBusy(undefined)
  }

  const selectPlan = (planId: string) => {
    if (planId === selectedId) return
    if (dirty && !window.confirm('Discard the unsaved changes to this plan?')) return
    setSelectedId(planId)
    setDetails(undefined)
    setEditor(undefined)
  }

  const statusFor = (planId: string) => {
    const planVersions = catalogVersions.filter(x => x.planId === planId)
    return {
      draft: planVersions.find(x => x.status === 'Draft'),
      published: planVersions.filter(x => x.status === 'Published').sort((a, b) => (b.version ?? 0) - (a.version ?? 0))[0],
    }
  }

  const tabs: { id: EditorTab; label: string; icon: typeof Eye; count?: number }[] = [
    { id: 'catalog', label: 'Catalog', icon: Eye },
    { id: 'pricing', label: 'Pricing', icon: BadgeDollarSign, count: editor?.prices.length },
    { id: 'features', label: 'Features', icon: ListChecks, count: editor?.features.length },
    { id: 'quotas', label: 'Usage quotas', icon: CircleGauge, count: editor?.quotas.length },
  ]

  return <Panel className="overflow-hidden">
    <div className="grid min-h-[720px] xl:grid-cols-[280px_minmax(0,1fr)]">
      <aside className="border-b border-slate-200 bg-slate-50/70 p-4 dark:border-white/10 dark:bg-[#081425]/60 xl:border-b-0 xl:border-r">
        <div className="px-2 pb-4"><p className="text-[10px] font-bold uppercase tracking-[.18em] text-slate-400">Plan catalog</p><p className="mt-2 text-xs leading-5 text-slate-500">Choose a plan to create or continue its next version.</p></div>
        <div className="space-y-2">{plans.map(plan => {
          const status = statusFor(plan.id ?? '')
          const selected = selectedId === plan.id
          return <button type="button" key={plan.id} onClick={() => selectPlan(plan.id ?? '')} className={`group w-full rounded-xl border p-3 text-left transition ${selected ? 'border-blue-200 bg-white shadow-[0_8px_24px_rgba(11,92,255,.08)] dark:border-blue-400/30 dark:bg-blue-400/[.08]' : 'border-transparent hover:border-slate-200 hover:bg-white dark:hover:border-white/10 dark:hover:bg-white/[.04]'}`}>
            <span className="flex items-start gap-3"><span className={`mt-0.5 grid h-8 w-8 shrink-0 place-items-center rounded-lg text-xs font-bold uppercase ${selected ? 'bg-[#0b5cff] text-white' : 'bg-slate-200 text-slate-500 dark:bg-white/10 dark:text-slate-300'}`}>{plan.name?.slice(0, 1)}</span><span className="min-w-0 flex-1"><span className="flex items-center gap-2"><span className="truncate text-sm font-semibold">{plan.name}</span>{status.draft && <span className="h-1.5 w-1.5 rounded-full bg-amber-400" title="Draft changes"/>}</span><span className="mt-1 block font-mono text-[10px] text-slate-400">{plan.code} · v{status.published?.version ?? '—'}</span></span><ChevronRight className={`mt-2 h-4 w-4 ${selected ? 'text-[#0b5cff]' : 'text-slate-300'}`}/></span>
          </button>
        })}</div>
        <div className="mt-5 rounded-xl border border-slate-200 bg-white p-4 dark:border-white/10 dark:bg-white/[.03]"><FileClock className="h-4 w-4 text-[#0b5cff]"/><p className="mt-3 text-xs font-semibold">Version-safe changes</p><p className="mt-1 text-[11px] leading-5 text-slate-500 dark:text-slate-400">Existing subscriptions remain pinned to the commercial terms they accepted.</p></div>
      </aside>

      <form onSubmit={save} className="min-w-0">
        {busy === 'load' || !editor || !details ? <div className="grid min-h-[720px] place-items-center"><div className="text-center"><span className="mx-auto block h-7 w-7 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]"/><p className="mt-3 text-xs text-slate-400">Loading plan configuration</p></div></div> : <>
          <header className="border-b border-slate-200 px-5 py-5 dark:border-white/10 lg:px-7">
            <div className="flex flex-col gap-5 2xl:flex-row 2xl:items-center 2xl:justify-between">
              <div><div className="flex flex-wrap items-center gap-2"><h2 className="text-xl font-semibold tracking-[-.03em]">{editor.name || details.plan!.code}</h2><StatusPill tone={details.hasDraft ? 'amber' : 'green'}>{details.hasDraft ? `Draft v${details.version!.version}` : `Published v${details.version!.version}`}</StatusPill>{dirty && <StatusPill tone="slate">Unsaved</StatusPill>}</div><div className="mt-2 flex flex-wrap items-center gap-4 text-xs text-slate-400"><span className="font-mono">{details.plan!.code}</span><span className="inline-flex items-center gap-1.5"><Users className="h-3.5 w-3.5"/>{details.activeSubscriptions} active subscription{details.activeSubscriptions === 1 ? '' : 's'}</span></div></div>
              <div className="flex flex-wrap gap-2"><button type="submit" disabled={!dirty || !!busy} className="inline-flex items-center gap-2 rounded-[10px] border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 shadow-sm transition hover:border-slate-400 disabled:cursor-not-allowed disabled:opacity-45 dark:border-white/15 dark:bg-white/5 dark:text-white"><Save className="h-4 w-4"/>{busy === 'save' ? 'Saving…' : 'Save draft'}</button><button type="button" onClick={publish} disabled={!details.hasDraft || dirty || !!busy} title={dirty ? 'Save the current changes before publishing' : undefined} className="inline-flex items-center gap-2 rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white shadow-[0_10px_24px_rgba(11,92,255,.22)] transition hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300 disabled:shadow-none dark:disabled:bg-slate-700"><Rocket className="h-4 w-4"/>{busy === 'publish' ? 'Publishing…' : 'Publish changes'}</button></div>
            </div>
            {notice && <div className={`mt-4 flex items-center gap-2 rounded-lg px-3 py-2.5 text-xs font-medium ${notice.tone === 'success' ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-400/10 dark:text-emerald-300' : 'bg-red-50 text-red-700 dark:bg-red-400/10 dark:text-red-300'}`}>{notice.tone === 'success' && <Check className="h-4 w-4"/>}{notice.text}</div>}
          </header>

          <nav className="flex gap-1 overflow-x-auto border-b border-slate-200 px-5 dark:border-white/10 lg:px-7" aria-label="Plan settings">{tabs.map(item => <button type="button" key={item.id} onClick={() => setTab(item.id)} className={`flex shrink-0 items-center gap-2 border-b-2 px-3 py-4 text-xs font-semibold transition ${tab === item.id ? 'border-[#0b5cff] text-[#0b5cff]' : 'border-transparent text-slate-400 hover:text-slate-700 dark:hover:text-white'}`}><item.icon className="h-4 w-4"/>{item.label}{item.count != null && <span className="rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] text-slate-500 dark:bg-white/10">{item.count}</span>}</button>)}</nav>

          <div className="p-5 lg:p-7">
            {tab === 'catalog' && <div className="mx-auto max-w-4xl space-y-7">
              <section><div className="mb-4"><h3 className="text-sm font-semibold">Customer-facing details</h3><p className="mt-1 text-xs text-slate-500 dark:text-slate-400">These fields appear in pricing, billing, and organization views.</p></div><div className="grid gap-5 md:grid-cols-[1fr_140px]"><label className={labelClass}>Plan name<input required value={editor.name} onChange={e => update(x => ({ ...x, name: e.target.value }))} className={fieldClass}/></label><label className={labelClass}>Display order<input required type="number" min="0" value={editor.displayOrder} onChange={e => update(x => ({ ...x, displayOrder: e.target.value }))} className={fieldClass}/></label></div><label className={`${labelClass} mt-5`}>Description<textarea required rows={4} value={editor.description} onChange={e => update(x => ({ ...x, description: e.target.value }))} className={fieldClass}/></label><label className={`${labelClass} mt-5 max-w-md`}>Stable plan code<input readOnly value={details.plan!.code ?? ''} className={`${fieldClass} cursor-not-allowed bg-slate-50 font-mono text-slate-400 dark:bg-white/[.02]`}/><span className="mt-2 block text-[11px] font-normal leading-4 text-slate-400">Codes are immutable because integrations may depend on them.</span></label></section>
              <section className="border-t border-slate-200 pt-7 dark:border-white/10"><h3 className="text-sm font-semibold">Trial offer</h3><p className="mt-1 text-xs text-slate-500 dark:text-slate-400">Give new subscriptions time to evaluate this plan before Stripe collects the first payment.</p><div className="mt-4 grid items-start gap-4 md:grid-cols-[minmax(0,1fr)_180px]"><Toggle checked={trialsEnabled && editor.trialEnabled} disabled={!trialsEnabled} onChange={value => update(x => ({ ...x, trialEnabled: value, trialDays: value && !x.trialDays ? String(defaultTrialDays) : x.trialDays }))} label={trialsEnabled ? 'Free trial' : 'Trials disabled globally'} description={trialsEnabled ? `Apply this trial to every new subscription. ${trialRequiresPaymentMethod ? 'Stripe will collect a payment method at signup.' : 'No payment method is required; trials without one cancel at expiry.'}` : 'Enable Saas.EnableTrials in appsettings.json before adding plan trials.'} icon={Sparkles}/>{trialsEnabled && editor.trialEnabled && <label className={labelClass}>Trial length (days)<input required type="number" min="1" max="365" value={editor.trialDays} onChange={e => update(x => ({ ...x, trialDays: e.target.value }))} className={fieldClass}/><span className="mt-2 block text-[11px] font-normal leading-4 text-slate-400">The deployment default is {defaultTrialDays} days.</span></label>}</div></section>
              <section className="border-t border-slate-200 pt-7 dark:border-white/10"><h3 className="text-sm font-semibold">Availability</h3><p className="mt-1 text-xs text-slate-500 dark:text-slate-400">Control how customers discover and buy this plan.</p><div className="mt-4 grid gap-3 md:grid-cols-2"><Toggle checked={editor.isPublic} onChange={value => update(x => ({ ...x, isPublic: value }))} label="Public plan" description="Show this plan on the pricing page." icon={editor.isPublic ? Eye : EyeOff}/><Toggle checked={editor.isContactSales} onChange={value => update(x => ({ ...x, isContactSales: value }))} label="Sales-assisted" description="Replace self-serve checkout with contact sales." icon={Users}/><Toggle checked={editor.isArchived} onChange={value => update(x => ({ ...x, isArchived: value }))} label="Archived" description="Hide this plan from all new customers." icon={Archive}/></div></section>
            </div>}

            {tab === 'pricing' && <div className="mx-auto max-w-5xl"><div className="mb-5 flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between"><div><div className="flex items-center gap-2"><h3 className="text-sm font-semibold">Prices & Stripe mapping</h3><span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${stripeConfigured ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-400/10 dark:text-emerald-300' : 'bg-amber-50 text-amber-700 dark:bg-amber-400/10 dark:text-amber-300'}`}>Stripe {stripeMode}</span></div><p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">Amounts use Stripe minor units: 4900 means $49.00 USD. Provisioning creates missing recurring Prices and fills their IDs automatically.</p></div><div className="flex shrink-0 flex-wrap gap-2"><button type="button" onClick={provisionStripe} disabled={!stripeCatalogProvisioningEnabled || dirty || !!busy || !editor.prices.some(x => x.isActive && Number(x.unitAmount) > 0 && !x.stripePriceId)} title={!stripeConfigured ? 'Set Stripe__SecretKey and restart the application' : dirty ? 'Save or discard local edits before creating Stripe objects' : !stripeCatalogProvisioningEnabled ? 'Catalog provisioning is disabled by Stripe configuration' : undefined} className="inline-flex items-center gap-2 rounded-lg border border-blue-200 bg-white px-3 py-2 text-xs font-semibold text-[#0b5cff] shadow-sm transition hover:bg-blue-50 disabled:cursor-not-allowed disabled:opacity-45 dark:border-blue-400/20 dark:bg-white/5 dark:hover:bg-blue-400/10"><Cloud className="h-3.5 w-3.5"/>{busy === 'stripe' ? 'Creating…' : 'Create missing in Stripe'}</button><button type="button" onClick={() => update(x => ({ ...x, prices: [...x.prices, { id: newId(), currency: 'usd', interval: BillingInterval.Month, unitAmount: '0', stripePriceId: '', isActive: true }] }))} className="inline-flex items-center gap-2 rounded-lg bg-blue-50 px-3 py-2 text-xs font-semibold text-[#0b5cff] dark:bg-blue-400/10"><Plus className="h-3.5 w-3.5"/>Add price</button></div></div>{editor.prices.length === 0 ? <EmptyState icon={BadgeDollarSign} title="No prices configured" body="Contact-sales plans may omit prices. Self-serve plans need at least one active monthly price." action={<button type="button" onClick={() => update(x => ({ ...x, prices: [{ id: newId(), currency: 'usd', interval: BillingInterval.Month, unitAmount: '0', stripePriceId: '', isActive: true }] }))} className="text-xs font-semibold text-[#0b5cff]">Add the first price →</button>}/> : <div className="space-y-3">{editor.prices.map((price, index) => <div key={price.id} className="grid items-end gap-3 rounded-xl border border-slate-200 bg-slate-50/50 p-4 dark:border-white/10 dark:bg-white/[.02] lg:grid-cols-[90px_120px_140px_minmax(180px,1fr)_auto_auto]"><label className={labelClass}>Currency<input required maxLength={3} value={price.currency} onChange={e => update(x => ({ ...x, prices: x.prices.map((row, i) => i === index ? { ...row, currency: e.target.value.toLowerCase() } : row) }))} className={`${fieldClass} font-mono uppercase`}/></label><label className={labelClass}>Interval<select value={price.interval} onChange={e => update(x => ({ ...x, prices: x.prices.map((row, i) => i === index ? { ...row, interval: e.target.value as BillingInterval } : row) }))} className={fieldClass}><option value={BillingInterval.Month}>Monthly</option><option value={BillingInterval.Year}>Annual</option></select></label><label className={labelClass}>Minor units<input required type="number" min="0" value={price.unitAmount} onChange={e => update(x => ({ ...x, prices: x.prices.map((row, i) => i === index ? { ...row, unitAmount: e.target.value } : row) }))} className={fieldClass}/></label><label className={labelClass}>Stripe Price ID<input placeholder="Created automatically or paste price_…" value={price.stripePriceId} onChange={e => update(x => ({ ...x, prices: x.prices.map((row, i) => i === index ? { ...row, stripePriceId: e.target.value } : row) }))} className={`${fieldClass} font-mono`}/></label><label className="flex h-[42px] items-center gap-2 text-xs font-semibold"><input type="checkbox" checked={price.isActive} onChange={e => update(x => ({ ...x, prices: x.prices.map((row, i) => i === index ? { ...row, isActive: e.target.checked } : row) }))} className="rounded border-slate-300 text-[#0b5cff] focus:ring-[#0b5cff]"/>Active</label><button type="button" title="Remove price" onClick={() => update(x => ({ ...x, prices: x.prices.filter((_, i) => i !== index) }))} className="grid h-[42px] w-[42px] place-items-center rounded-lg text-slate-400 transition hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-400/10"><Trash2 className="h-4 w-4"/></button></div>)}</div>}</div>}

            {tab === 'features' && <div className="mx-auto max-w-5xl"><div className="mb-5 flex items-end justify-between gap-4"><div><h3 className="text-sm font-semibold">Customer-facing features</h3><p className="mt-1 text-xs text-slate-500 dark:text-slate-400">Features are displayed in this order on pricing and billing surfaces.</p></div><button type="button" onClick={() => update(x => ({ ...x, features: [...x.features, { id: newId(), key: '', name: '', description: '', enabled: true }] }))} className="inline-flex shrink-0 items-center gap-2 rounded-lg bg-blue-50 px-3 py-2 text-xs font-semibold text-[#0b5cff] dark:bg-blue-400/10"><Plus className="h-3.5 w-3.5"/>Add feature</button></div>{editor.features.length === 0 ? <EmptyState icon={Sparkles} title="No features yet" body="Add the outcomes customers receive with this plan. Keys remain machine-readable for entitlement checks." action={<button type="button" onClick={() => update(x => ({ ...x, features: [{ id: newId(), key: '', name: '', description: '', enabled: true }] }))} className="text-xs font-semibold text-[#0b5cff]">Add the first feature →</button>}/> : <div className="space-y-3">{editor.features.map((feature, index) => <div key={feature.id} className="grid items-end gap-3 rounded-xl border border-slate-200 bg-slate-50/50 p-4 dark:border-white/10 dark:bg-white/[.02] lg:grid-cols-[auto_180px_minmax(180px,.8fr)_minmax(220px,1.2fr)_auto_auto]"><GripVertical className="mb-3 h-4 w-4 text-slate-300"/><label className={labelClass}>Feature key<input required placeholder="advanced.analytics" value={feature.key} onChange={e => update(x => ({ ...x, features: x.features.map((row, i) => i === index ? { ...row, key: e.target.value } : row) }))} className={`${fieldClass} font-mono`}/></label><label className={labelClass}>Name<input required placeholder="Advanced analytics" value={feature.name} onChange={e => update(x => ({ ...x, features: x.features.map((row, i) => i === index ? { ...row, name: e.target.value } : row) }))} className={fieldClass}/></label><label className={labelClass}>Description<input placeholder="Optional supporting detail" value={feature.description} onChange={e => update(x => ({ ...x, features: x.features.map((row, i) => i === index ? { ...row, description: e.target.value } : row) }))} className={fieldClass}/></label><label className="flex h-[42px] items-center gap-2 text-xs font-semibold"><input type="checkbox" checked={feature.enabled} onChange={e => update(x => ({ ...x, features: x.features.map((row, i) => i === index ? { ...row, enabled: e.target.checked } : row) }))} className="rounded border-slate-300 text-[#0b5cff] focus:ring-[#0b5cff]"/>Enabled</label><button type="button" title="Remove feature" onClick={() => update(x => ({ ...x, features: x.features.filter((_, i) => i !== index) }))} className="grid h-[42px] w-[42px] place-items-center rounded-lg text-slate-400 transition hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-400/10"><Trash2 className="h-4 w-4"/></button></div>)}</div>}</div>}

            {tab === 'quotas' && <div className="mx-auto max-w-5xl"><div className="mb-5 flex items-end justify-between gap-4"><div><h3 className="text-sm font-semibold">Metered usage quotas</h3><p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">Meter keys connect product resources such as stored documents, bytes, API requests, and seats to plan allowances. Leave allowance blank for unlimited usage.</p></div><button type="button" onClick={() => update(x => ({ ...x, quotas: [...x.quotas, { id: newId(), meterKey: '', displayName: '', includedUnits: '', enforcement: QuotaEnforcement.HardLimit, rolloverEnabled: false }] }))} className="inline-flex shrink-0 items-center gap-2 rounded-lg bg-blue-50 px-3 py-2 text-xs font-semibold text-[#0b5cff] dark:bg-blue-400/10"><Plus className="h-3.5 w-3.5"/>Add quota</button></div>{editor.quotas.length === 0 ? <EmptyState icon={CircleGauge} title="No usage quotas" body="Add a quota for each resource this plan should meter." action={<button type="button" onClick={() => update(x => ({ ...x, quotas: [{ id: newId(), meterKey: '', displayName: '', includedUnits: '', enforcement: QuotaEnforcement.HardLimit, rolloverEnabled: false }] }))} className="text-xs font-semibold text-[#0b5cff]">Add the first quota →</button>}/> : <div className="space-y-3">{editor.quotas.map((quota, index) => <div key={quota.id} className="grid items-end gap-3 rounded-xl border border-slate-200 bg-slate-50/50 p-4 dark:border-white/10 dark:bg-white/[.02] lg:grid-cols-[minmax(160px,1fr)_minmax(150px,.8fr)_130px_170px_auto_auto]"><label className={labelClass}>Meter key<input required placeholder="documents.stored" value={quota.meterKey} onChange={e => update(x => ({ ...x, quotas: x.quotas.map((row, i) => i === index ? { ...row, meterKey: e.target.value } : row) }))} className={`${fieldClass} font-mono`}/></label><label className={labelClass}>Display name<input required placeholder="Documents stored" value={quota.displayName} onChange={e => update(x => ({ ...x, quotas: x.quotas.map((row, i) => i === index ? { ...row, displayName: e.target.value } : row) }))} className={fieldClass}/></label><label className={labelClass}>Allowance<input type="number" min="0" placeholder="Unlimited" value={quota.includedUnits} onChange={e => update(x => ({ ...x, quotas: x.quotas.map((row, i) => i === index ? { ...row, includedUnits: e.target.value } : row) }))} className={fieldClass}/></label><label className={labelClass}>Enforcement<select value={quota.enforcement} onChange={e => update(x => ({ ...x, quotas: x.quotas.map((row, i) => i === index ? { ...row, enforcement: e.target.value as QuotaEnforcement } : row) }))} className={fieldClass}><option value={QuotaEnforcement.HardLimit}>Hard limit</option><option value={QuotaEnforcement.SoftLimit}>Soft limit</option><option value={QuotaEnforcement.MeteredOverage}>Metered overage</option></select></label><label className="flex h-[42px] items-center gap-2 text-xs font-semibold"><input type="checkbox" checked={quota.rolloverEnabled} onChange={e => update(x => ({ ...x, quotas: x.quotas.map((row, i) => i === index ? { ...row, rolloverEnabled: e.target.checked } : row) }))} className="rounded border-slate-300 text-[#0b5cff] focus:ring-[#0b5cff]"/>Rollover</label><button type="button" title="Remove quota" onClick={() => update(x => ({ ...x, quotas: x.quotas.filter((_, i) => i !== index) }))} className="grid h-[42px] w-[42px] place-items-center rounded-lg text-slate-400 transition hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-400/10"><Trash2 className="h-4 w-4"/></button></div>)}</div>}</div>}
          </div>
        </>}
      </form>
    </div>
  </Panel>
}
