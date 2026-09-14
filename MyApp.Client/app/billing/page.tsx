'use client'

import Link from 'next/link'
import { useClient } from '@servicestack/react'
import { ArrowRight, CalendarDays, CheckCircle2, CreditCard, ExternalLink, LoaderCircle, ReceiptText, ShieldCheck } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import AppShell, { PageHeading, Panel, StatusPill } from '@/components/app-shell'
import { ValidateAuth } from '@/lib/auth'
import { ConfirmCheckoutSession, CreateCustomerPortalSession } from '@/lib/dtos'
import { LoadingPanel, useSaasDashboard } from '@/lib/use-saas'

type CheckoutState = 'idle' | 'confirming' | 'confirmed' | 'error'

function BillingPage() {
  const client = useClient()
  const { data, error, loading, refresh } = useSaasDashboard()
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string>()
  const [checkoutState, setCheckoutState] = useState<CheckoutState>('idle')
  const checkoutStarted = useRef(false)

  useEffect(() => {
    if (checkoutStarted.current || typeof window === 'undefined') return
    const query = new URLSearchParams(window.location.search)
    if (query.get('checkout') !== 'success') return

    checkoutStarted.current = true
    setCheckoutState('confirming')
    const sessionId = query.get('session_id') || undefined

    void client.api(new ConfirmCheckoutSession({ sessionId })).then(async api => {
      if (!api.succeeded || !api.response?.confirmed) {
        setCheckoutState('error')
        setNotice(api.error?.message || 'Stripe has not completed this Checkout Session yet. Verify webhook forwarding and try again.')
        return
      }
      await refresh()
      setCheckoutState('confirmed')
      window.history.replaceState(null, '', '/billing')
    })
  }, [client, refresh])

  const portal = async () => {
    setBusy(true)
    setNotice(undefined)
    const api = await client.api(new CreateCustomerPortalSession())
    if (api.succeeded && api.response?.url) window.location.href = api.response.url
    else setNotice(api.error?.message || 'Unable to open the Stripe billing portal.')
    setBusy(false)
  }

  if (loading && !data) return <AppShell><LoadingPanel /></AppShell>

  return <AppShell workspaceName={data?.workspace?.name}>
    <PageHeading
      eyebrow="Commercial relationship"
      title="Plans & billing"
      description="Manage your organization’s plan. Stripe securely handles payment details, invoices, credits, and tax."
      action={<button onClick={portal} disabled={busy} className="inline-flex items-center gap-2 rounded-[10px] border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold disabled:opacity-50 dark:border-white/15 dark:bg-white/5">
        Open billing portal <ExternalLink className="h-4 w-4" />
      </button>}
    />

    {checkoutState === 'confirming' && <Panel className="mb-5 flex items-center gap-3 border-blue-200 bg-blue-50 p-4 text-sm text-blue-900 dark:border-blue-400/20 dark:bg-blue-400/10 dark:text-blue-100">
      <LoaderCircle className="h-5 w-5 shrink-0 animate-spin text-[#0b5cff]" />
      <div><p className="font-semibold">Activating your subscription</p><p className="mt-0.5 text-blue-700 dark:text-blue-200/75">Confirming the completed Checkout Session with Stripe…</p></div>
    </Panel>}
    {checkoutState === 'confirmed' && <Panel className="mb-5 flex items-center gap-3 border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900 dark:border-emerald-400/20 dark:bg-emerald-400/10 dark:text-emerald-100">
      <CheckCircle2 className="h-5 w-5 shrink-0 text-emerald-600" />
      <div><p className="font-semibold">Subscription activated</p><p className="mt-0.5 text-emerald-700 dark:text-emerald-200/75">Your plan and quotas are ready to use.</p></div>
    </Panel>}
    {(error || notice) && <Panel className="mb-5 border-amber-200 bg-amber-50 p-4 text-sm text-amber-800 dark:border-amber-400/20 dark:bg-amber-400/10 dark:text-amber-200">{error || notice}</Panel>}

    <div className="grid gap-5 xl:grid-cols-[1.3fr_.7fr]">
      <Panel className="overflow-hidden">
        <div className="flex items-center justify-between border-b border-slate-200 px-6 py-5 dark:border-white/10">
          <div><h2 className="font-semibold">Current plan</h2><p className="mt-1 text-xs text-slate-400">Pinned to an immutable plan version</p></div>
          <StatusPill tone={data?.subscription?.status === 'Free' ? 'slate' : 'green'}>{data?.subscription?.status}</StatusPill>
        </div>
        <div className="p-6">
          <div className="flex flex-col justify-between gap-6 sm:flex-row">
            <div><p className="text-3xl font-semibold tracking-[-.045em]">{data?.plan?.name}</p><p className="mt-2 max-w-lg text-sm leading-6 text-slate-500 dark:text-slate-400">{data?.plan?.description}</p></div>
            <div className="sm:text-right"><p className="text-xs text-slate-400">Billing interval</p><p className="mt-1 text-sm font-semibold">{data?.subscription?.interval}</p></div>
          </div>
          <div className="mt-7 grid gap-3 sm:grid-cols-2">{data?.plan?.features?.map(x => <div key={x} className="flex items-center gap-2 rounded-lg bg-slate-50 p-3 text-sm dark:bg-white/[.035]"><ShieldCheck className="h-4 w-4 text-emerald-500" />{x}</div>)}</div>
          <div className="mt-7 flex flex-wrap gap-3"><Link href="/pricing" className="inline-flex items-center gap-2 rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white">Compare plans <ArrowRight className="h-4 w-4" /></Link></div>
        </div>
      </Panel>
      <div className="space-y-5">
        <Panel className="p-6"><CreditCard className="h-5 w-5 text-[#0b5cff]" /><h2 className="mt-4 font-semibold">Payment method</h2><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Payment methods are stored and managed securely by Stripe.</p><button onClick={portal} disabled={busy} className="mt-4 text-sm font-semibold text-[#0b5cff] disabled:opacity-50">Manage in Stripe →</button></Panel>
        <Panel className="p-6"><ReceiptText className="h-5 w-5 text-[#0b5cff]" /><h2 className="mt-4 font-semibold">Invoices</h2><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Download tax-ready invoice PDFs from the hosted billing portal.</p></Panel>
      </div>
    </div>
    <Panel className="mt-5 p-6"><div className="flex items-center gap-3"><CalendarDays className="h-5 w-5 text-[#0b5cff]" /><div><h2 className="font-semibold">Current billing period</h2><p className="mt-1 text-sm text-slate-500 dark:text-slate-400">{data?.subscription?.periodStart ? new Date(data.subscription.periodStart).toLocaleDateString() : '—'} — {data?.subscription?.periodEnd ? new Date(data.subscription.periodEnd).toLocaleDateString() : '—'}</p></div></div></Panel>
  </AppShell>
}

export default ValidateAuth(BillingPage)
