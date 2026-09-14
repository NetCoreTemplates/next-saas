import Link from 'next/link'
import { LockKeyhole } from 'lucide-react'
import { EffectiveEntitlementInfo } from '@/lib/dtos'
import { Panel } from '@/components/app-shell'

export function hasFeature(entitlements: EffectiveEntitlementInfo[] | undefined, key: string) {
  return entitlements?.some(x => x.key === key && x.enabled) === true
}

export default function FeatureGate({ feature, entitlements, children, title = 'Available on a higher plan' }: {
  feature: string
  entitlements?: EffectiveEntitlementInfo[]
  children: React.ReactNode
  title?: string
}) {
  if (hasFeature(entitlements, feature)) return children
  return <Panel className="grid min-h-72 place-items-center p-8 text-center">
    <div className="max-w-md"><span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><LockKeyhole className="h-5 w-5"/></span><h2 className="mt-5 text-lg font-semibold">{title}</h2><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Your effective entitlement for <code className="rounded bg-slate-100 px-1.5 py-0.5 text-xs dark:bg-white/10">{feature}</code> is disabled. Plan and customer overrides are applied automatically.</p><Link href="/billing" className="mt-5 inline-flex rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white">Compare plans</Link></div>
  </Panel>
}
