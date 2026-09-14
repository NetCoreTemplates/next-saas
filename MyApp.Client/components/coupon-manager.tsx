'use client'

import { FormEvent, useEffect, useRef, useState } from 'react'
import { Ban, CalendarDays, Check, Plus, ShieldCheck, Sparkles, TicketPercent } from 'lucide-react'
import { Panel, StatusPill } from '@/components/app-shell'
import { client } from '@/lib/gateway'
import {
  CouponDuration,
  CreateSaasCoupon,
  DeactivateSaasCoupon,
  GetSaasCoupons,
  SaasCouponInfo,
} from '@/lib/dtos'

type DiscountType = 'percent' | 'amount'
type CouponForm = {
  code: string
  name: string
  discountType: DiscountType
  percentOff: string
  amountOff: string
  currency: string
  duration: CouponDuration
  durationInMonths: string
  maxRedemptions: string
  expiresAt: string
  firstTimeTransaction: boolean
}

const emptyForm = (): CouponForm => ({
  code: '', name: '', discountType: 'percent', percentOff: '20', amountOff: '', currency: 'usd',
  duration: CouponDuration.Once, durationInMonths: '3', maxRedemptions: '', expiresAt: '',
  firstTimeTransaction: false,
})
const fieldClass = 'mt-2 block w-full rounded-[10px] border-slate-200 bg-white text-sm shadow-sm transition focus:border-[#0b5cff] focus:ring-[#0b5cff]/20 dark:border-white/10 dark:bg-[#0c1729]'
const labelClass = 'block text-xs font-semibold text-slate-600 dark:text-slate-300'

function discountLabel(coupon: SaasCouponInfo) {
  if (coupon.percentOff != null) return `${coupon.percentOff}% off`
  const currency = (coupon.currency ?? '').toUpperCase()
  return `${coupon.amountOff?.toLocaleString() ?? 0} ${currency} minor units off`
}

function durationLabel(coupon: SaasCouponInfo) {
  if (coupon.duration === CouponDuration.Forever) return 'Every invoice'
  if (coupon.duration === CouponDuration.Repeating) return `${coupon.durationInMonths ?? 0} billing months`
  return 'First invoice'
}

function dateLabel(value?: string) {
  return value ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(value)) : 'No expiry'
}

export function CouponManager() {
  const [coupons, setCoupons] = useState<SaasCouponInfo[]>([])
  const [stripeConfigured, setStripeConfigured] = useState(true)
  const [form, setForm] = useState<CouponForm>(emptyForm)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState<string>()
  const [notice, setNotice] = useState<{ tone: 'success' | 'error'; text: string }>()
  const loaded = useRef(false)

  const load = () => {
    setLoading(true)
    const request = client.api(new GetSaasCoupons())
    request.then(api => {
      if (api.succeeded && api.response) {
        setCoupons(api.response.results ?? [])
        setStripeConfigured(api.response.stripeConfigured ?? false)
      } else {
        setNotice({ tone: 'error', text: api.error?.message ?? 'Unable to load promotion codes.' })
      }
    }).catch(() => {
      setNotice({ tone: 'error', text: 'Unable to load promotion codes.' })
    }).finally(() => {
      setLoading(false)
    })
    return request
  }

  useEffect(() => {
    if (loaded.current) return
    loaded.current = true
    load()
  }, [])

  const create = async (event: FormEvent) => {
    event.preventDefault()
    setBusy('create')
    setNotice(undefined)
    const api = await client.api(new CreateSaasCoupon({
      code: form.code,
      name: form.name,
      percentOff: form.discountType === 'percent' ? Number(form.percentOff) : undefined,
      amountOff: form.discountType === 'amount' ? Number(form.amountOff) : undefined,
      currency: form.discountType === 'amount' ? form.currency : undefined,
      duration: form.duration,
      durationInMonths: form.duration === CouponDuration.Repeating ? Number(form.durationInMonths) : undefined,
      maxRedemptions: form.maxRedemptions ? Number(form.maxRedemptions) : undefined,
      expiresAt: form.expiresAt ? new Date(form.expiresAt).toISOString() : undefined,
      firstTimeTransaction: form.firstTimeTransaction,
    }))
    if (api.succeeded && api.response) {
      setCoupons(current => [api.response!, ...current])
      setForm(emptyForm())
      setNotice({ tone: 'success', text: `${api.response.code} is ready to use in Stripe Checkout.` })
    } else {
      setNotice({ tone: 'error', text: api.error?.message ?? 'Unable to create this promotion code.' })
    }
    setBusy(undefined)
  }

  const deactivate = async (coupon: SaasCouponInfo) => {
    if (!coupon.promotionCodeId || !window.confirm(`Deactivate ${coupon.code}? Customers will no longer be able to redeem it.`)) return
    setBusy(coupon.promotionCodeId)
    setNotice(undefined)
    const api = await client.api(new DeactivateSaasCoupon({ promotionCodeId: coupon.promotionCodeId }))
    if (api.succeeded && api.response) {
      setCoupons(current => current.map(x => x.promotionCodeId === api.response!.promotionCodeId ? api.response! : x))
      setNotice({ tone: 'success', text: `${coupon.code} has been deactivated. Existing discounted subscriptions are unchanged.` })
    } else {
      setNotice({ tone: 'error', text: api.error?.message ?? 'Unable to deactivate this promotion code.' })
    }
    setBusy(undefined)
  }

  return <Panel className="mt-5 overflow-hidden">
    <header className="border-b border-slate-200 px-6 py-5 dark:border-white/10">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex items-start gap-3"><span className="grid h-10 w-10 shrink-0 place-items-center rounded-[10px] bg-violet-50 text-violet-600 dark:bg-violet-400/10 dark:text-violet-300"><TicketPercent className="h-5 w-5"/></span><div><h2 className="font-semibold">Coupons & promotion codes</h2><p className="mt-1 max-w-2xl text-xs leading-5 text-slate-500 dark:text-slate-400">Create Stripe-backed discounts customers can enter during hosted Checkout. Deactivation prevents new redemptions without changing existing subscriptions.</p></div></div>
        <StatusPill tone={stripeConfigured ? 'green' : 'amber'}>{stripeConfigured ? 'Stripe connected' : 'Setup required'}</StatusPill>
      </div>
      {notice && <div className={`mt-4 flex items-center gap-2 rounded-lg px-3 py-2.5 text-xs font-medium ${notice.tone === 'success' ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-400/10 dark:text-emerald-300' : 'bg-red-50 text-red-700 dark:bg-red-400/10 dark:text-red-300'}`}>{notice.tone === 'success' && <Check className="h-4 w-4"/>}{notice.text}</div>}
    </header>

    {!stripeConfigured && !loading ? <div className="grid min-h-72 place-items-center p-8 text-center"><div className="max-w-md"><span className="mx-auto grid h-12 w-12 place-items-center rounded-xl bg-amber-50 text-amber-600 dark:bg-amber-400/10 dark:text-amber-300"><ShieldCheck className="h-6 w-6"/></span><h3 className="mt-4 font-semibold">Connect Stripe to manage discounts</h3><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Set <code className="rounded bg-slate-100 px-1.5 py-0.5 text-xs dark:bg-white/10">Stripe__SecretKey</code>, then restart the application. Coupon benefits and redemption limits remain authoritative in Stripe.</p></div></div> : <div className="grid xl:grid-cols-[390px_minmax(0,1fr)]">
      <form onSubmit={create} className="border-b border-slate-200 bg-slate-50/60 p-6 dark:border-white/10 dark:bg-[#081425]/50 xl:border-b-0 xl:border-r">
        <div><p className="text-[10px] font-bold uppercase tracking-[.18em] text-slate-400">New offer</p><h3 className="mt-2 text-sm font-semibold">Create a promotion code</h3></div>
        <div className="mt-5 grid gap-4 sm:grid-cols-2 xl:grid-cols-1">
          <label className={labelClass}>Customer code<input required minLength={3} maxLength={50} pattern="[A-Za-z0-9_-]+" placeholder="WELCOME20" value={form.code} onChange={e => setForm(x => ({ ...x, code: e.target.value.toUpperCase() }))} className={`${fieldClass} font-mono uppercase`}/></label>
          <label className={labelClass}>Internal name<input required placeholder="New customer launch offer" value={form.name} onChange={e => setForm(x => ({ ...x, name: e.target.value }))} className={fieldClass}/></label>
        </div>

        <fieldset className="mt-5"><legend className={labelClass}>Discount</legend><div className="mt-2 grid grid-cols-2 gap-2">{(['percent', 'amount'] as DiscountType[]).map(type => <button type="button" key={type} onClick={() => setForm(x => ({ ...x, discountType: type }))} className={`rounded-[10px] border px-3 py-2 text-xs font-semibold transition ${form.discountType === type ? 'border-[#0b5cff] bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10' : 'border-slate-200 bg-white text-slate-500 dark:border-white/10 dark:bg-white/[.03]'}`}>{type === 'percent' ? 'Percentage' : 'Fixed amount'}</button>)}</div></fieldset>
        {form.discountType === 'percent' ? <label className={`${labelClass} mt-4`}>Percent off<input required type="number" min="0.01" max="100" step="0.01" value={form.percentOff} onChange={e => setForm(x => ({ ...x, percentOff: e.target.value }))} className={fieldClass}/></label> : <div className="mt-4 grid grid-cols-[1fr_100px] gap-3"><label className={labelClass}>Amount off <span className="font-normal text-slate-400">(minor units)</span><input required type="number" min="1" step="1" placeholder="2000" value={form.amountOff} onChange={e => setForm(x => ({ ...x, amountOff: e.target.value }))} className={fieldClass}/></label><label className={labelClass}>Currency<input required minLength={3} maxLength={3} value={form.currency} onChange={e => setForm(x => ({ ...x, currency: e.target.value.toLowerCase() }))} className={`${fieldClass} font-mono uppercase`}/></label></div>}

        <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-1"><label className={labelClass}>Applies to<select value={form.duration} onChange={e => setForm(x => ({ ...x, duration: e.target.value as CouponDuration }))} className={fieldClass}><option value={CouponDuration.Once}>First invoice only</option><option value={CouponDuration.Repeating}>A fixed number of months</option><option value={CouponDuration.Forever}>Every invoice</option></select></label>{form.duration === CouponDuration.Repeating && <label className={labelClass}>Billing months<input required type="number" min="1" max="36" value={form.durationInMonths} onChange={e => setForm(x => ({ ...x, durationInMonths: e.target.value }))} className={fieldClass}/></label>}</div>
        <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-1"><label className={labelClass}>Maximum redemptions <span className="font-normal text-slate-400">(optional)</span><input type="number" min="1" placeholder="Unlimited" value={form.maxRedemptions} onChange={e => setForm(x => ({ ...x, maxRedemptions: e.target.value }))} className={fieldClass}/></label><label className={labelClass}>Expires at <span className="font-normal text-slate-400">(optional)</span><input type="datetime-local" value={form.expiresAt} onChange={e => setForm(x => ({ ...x, expiresAt: e.target.value }))} className={fieldClass}/></label></div>
        <label className="mt-5 flex cursor-pointer items-start gap-3 rounded-xl border border-slate-200 bg-white p-4 dark:border-white/10 dark:bg-white/[.03]"><input type="checkbox" checked={form.firstTimeTransaction} onChange={e => setForm(x => ({ ...x, firstTimeTransaction: e.target.checked }))} className="mt-0.5 rounded border-slate-300 text-[#0b5cff] focus:ring-[#0b5cff]"/><span><span className="block text-xs font-semibold">New customers only</span><span className="mt-1 block text-[11px] leading-4 text-slate-400">Restrict redemption to customers without a prior Stripe transaction.</span></span></label>
        <button disabled={busy === 'create' || loading} className="mt-5 inline-flex w-full items-center justify-center gap-2 rounded-[10px] bg-[#0b5cff] px-4 py-3 text-sm font-semibold text-white shadow-[0_10px_24px_rgba(11,92,255,.22)] transition hover:bg-blue-700 disabled:opacity-50"><Plus className="h-4 w-4"/>{busy === 'create' ? 'Creating in Stripe…' : 'Create promotion code'}</button>
      </form>

      <section className="min-w-0 p-6">
        <div className="flex items-end justify-between gap-4"><div><p className="text-[10px] font-bold uppercase tracking-[.18em] text-slate-400">Offer catalog</p><h3 className="mt-2 text-sm font-semibold">Available promotion codes</h3></div><span className="text-xs text-slate-400">{coupons.length} total</span></div>
        {loading ? <div className="grid min-h-72 place-items-center"><span className="h-7 w-7 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]"/></div> : coupons.length === 0 ? <div className="mt-5 grid min-h-64 place-items-center rounded-xl border border-dashed border-slate-300 bg-slate-50/50 p-8 text-center dark:border-white/15 dark:bg-white/[.02]"><div><Sparkles className="mx-auto h-6 w-6 text-violet-400"/><h4 className="mt-3 text-sm font-semibold">No promotion codes yet</h4><p className="mt-2 max-w-sm text-xs leading-5 text-slate-500">Create a focused launch, retention, or partner offer. Customers redeem it securely in Stripe Checkout.</p></div></div> : <div className="mt-5 space-y-3">{coupons.map(coupon => {
          const usable = coupon.active && coupon.valid
          return <article key={coupon.promotionCodeId} className="rounded-xl border border-slate-200 bg-white p-4 shadow-[0_4px_16px_rgba(15,23,42,.03)] dark:border-white/10 dark:bg-white/[.025]">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between"><div className="min-w-0"><div className="flex flex-wrap items-center gap-2"><code className="rounded-md bg-violet-50 px-2 py-1 text-sm font-bold tracking-wide text-violet-700 dark:bg-violet-400/10 dark:text-violet-300">{coupon.code}</code><StatusPill tone={usable ? 'green' : 'slate'}>{usable ? 'Active' : 'Inactive'}</StatusPill>{coupon.livemode === false && <StatusPill tone="amber">Test mode</StatusPill>}</div><p className="mt-2 truncate text-sm font-semibold">{coupon.name}</p><div className="mt-2 flex flex-wrap gap-x-5 gap-y-1 text-[11px] text-slate-400"><span className="font-semibold text-slate-600 dark:text-slate-300">{discountLabel(coupon)}</span><span>{durationLabel(coupon)}</span><span>{coupon.timesRedeemed ?? 0}{coupon.maxRedemptions ? ` / ${coupon.maxRedemptions}` : ''} redeemed</span></div></div><div className="flex shrink-0 items-center gap-3"><div className="text-right text-[11px] text-slate-400"><span className="flex items-center gap-1.5"><CalendarDays className="h-3.5 w-3.5"/>{dateLabel(coupon.expiresAt)}</span>{coupon.firstTimeTransaction && <span className="mt-1 block">New customers only</span>}</div>{usable && <button type="button" title="Deactivate promotion code" disabled={busy === coupon.promotionCodeId} onClick={() => deactivate(coupon)} className="grid h-9 w-9 place-items-center rounded-lg border border-slate-200 text-slate-400 transition hover:border-red-200 hover:bg-red-50 hover:text-red-600 disabled:opacity-50 dark:border-white/10 dark:hover:bg-red-400/10"><Ban className="h-4 w-4"/></button>}</div></div>
          </article>
        })}</div>}
      </section>
    </div>}
  </Panel>
}
