'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import { GetSaasDashboard, GetSaasDashboardResponse, GetUsageAnalytics, GetUsageAnalyticsResponse } from '@/lib/dtos'
import { client } from '@/lib/gateway'

export function useSaasDashboard() {
  const [data,setData] = useState<GetSaasDashboardResponse>()
  const [error,setError] = useState<string>()
  const [loading,setLoading] = useState(true)
  const initialized = useRef(false)
  const refresh = useCallback(async () => {
    setLoading(true)
    const api = await client.api(new GetSaasDashboard())
    if (api.succeeded) { setData(api.response); setError(undefined) }
    else setError(api.error?.message || 'Unable to load workspace')
    setLoading(false)
  },[])
  useEffect(() => {
    if (initialized.current) return
    initialized.current = true
    void refresh()
  },[refresh])
  return { data,error,loading,refresh }
}

export function useUsageAnalytics(meterKey: string, days = 30, enabled = true, revision = '') {
  const [data,setData] = useState<GetUsageAnalyticsResponse>()
  const [error,setError] = useState<string>()
  const [loading,setLoading] = useState(false)
  const request = useRef({ key: '', id: 0 })
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])

  useEffect(() => {
    if (!enabled || !meterKey) return
    const key = `${meterKey}:${days}:${revision}`
    if (request.current.key === key) return

    const id = request.current.id + 1
    request.current = { key, id }
    setLoading(true)
    setError(undefined)
    void client.api(new GetUsageAnalytics({ meterKey, days })).then(api => {
      if (!mounted.current || request.current.id !== id) return
      if (api.succeeded) setData(api.response)
      else setError(api.error?.message || 'Unable to load usage analytics')
      setLoading(false)
    })
  }, [days,enabled,meterKey,revision])

  return { data,error,loading }
}

export function LoadingPanel() { return <div className="grid min-h-72 place-items-center rounded-[14px] border border-slate-200 bg-white dark:border-white/10 dark:bg-white/[.035]"><div className="text-center"><span className="mx-auto block h-7 w-7 animate-spin rounded-full border-2 border-blue-200 border-t-[#0b5cff]"/><p className="mt-3 text-xs text-slate-400">Loading organization</p></div></div> }
