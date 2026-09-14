'use client'

import { Suspense, useContext, useEffect, useState } from 'react'
import { useRouter, useSearchParams } from 'next/navigation'
import { useAuth, useClient } from '@servicestack/react'
import { CheckCircle2, MailCheck } from 'lucide-react'
import AppShell, { Panel } from '@/components/app-shell'
import { AuthReadyContext } from '@/app/providers'
import { AcceptWorkspaceInvitation } from '@/lib/dtos'
import { Routes } from '@/lib/gateway'

function InvitationContent() {
  const client = useClient()
  const router = useRouter()
  const search = useSearchParams()
  const token = search.get('token') ?? ''
  const authReady = useContext(AuthReadyContext)
  const { isAuthenticated } = useAuth()
  const [busy, setBusy] = useState(false)
  const [accepted, setAccepted] = useState(false)
  const [message, setMessage] = useState<string>()

  useEffect(() => {
    if (authReady && !isAuthenticated) {
      router.replace(Routes.signin(`/team/invite?token=${encodeURIComponent(token)}`))
    }
  }, [authReady, isAuthenticated, router, token])

  const accept = async () => {
    if (!token) { setMessage('This invitation link is incomplete.'); return }
    setBusy(true)
    const api = await client.api(new AcceptWorkspaceInvitation({ token }))
    setBusy(false)
    if (api.succeeded) {
      setAccepted(true)
      setMessage(`You joined ${api.response?.workspace?.name ?? 'the organization'}.`)
    } else setMessage(api.error?.message ?? 'The invitation could not be accepted.')
  }

  if (!authReady || !isAuthenticated) return <AppShell><div className="grid min-h-[55vh] place-items-center"><span className="h-7 w-7 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]" /></div></AppShell>

  return <AppShell>
    <div className="mx-auto grid min-h-[65vh] max-w-xl place-items-center px-4 py-12">
      <Panel className="w-full p-8 text-center sm:p-10">
        <span className={`mx-auto grid h-14 w-14 place-items-center rounded-2xl ${accepted ? 'bg-emerald-50 text-emerald-600 dark:bg-emerald-400/10' : 'bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10'}`}>{accepted ? <CheckCircle2 className="h-7 w-7" /> : <MailCheck className="h-7 w-7" />}</span>
        <p className="mt-6 text-xs font-bold uppercase tracking-[.16em] text-[#0b5cff]">Organization invitation</p>
        <h1 className="mt-2 text-3xl font-semibold tracking-[-.04em]">{accepted ? 'Invitation accepted' : 'Join your team'}</h1>
        <p className="mx-auto mt-4 max-w-md text-sm leading-6 text-slate-500 dark:text-slate-400">{message ?? 'Accepting gives your signed-in account access using the role selected by the organization administrator.'}</p>
        {accepted
          ? <button onClick={() => router.replace('/dashboard')} className="mt-7 rounded-[10px] bg-[#0b5cff] px-5 py-3 text-sm font-semibold text-white">Open organization</button>
          : <button disabled={busy || !token} onClick={() => void accept()} className="mt-7 rounded-[10px] bg-[#0b5cff] px-5 py-3 text-sm font-semibold text-white disabled:opacity-50">{busy ? 'Accepting…' : 'Accept invitation'}</button>}
      </Panel>
    </div>
  </AppShell>
}

export default function InvitationPage() {
  return <Suspense fallback={<AppShell><div className="min-h-[65vh]" /></AppShell>}><InvitationContent /></Suspense>
}
