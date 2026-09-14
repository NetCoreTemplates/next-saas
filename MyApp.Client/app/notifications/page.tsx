'use client'

import { useCallback, useEffect, useState } from 'react'
import { Bell, Check, Mail, SlidersHorizontal } from 'lucide-react'
import AppShell, { PageHeading, Panel, StatusPill } from '@/components/app-shell'
import { ValidateAuth } from '@/lib/auth'
import { client } from '@/lib/gateway'
import { GetNotificationPreferences, MarkNotificationRead, NotificationChannel, NotificationDelivery, NotificationPreferenceInput, QueryNotifications, UpdateNotificationPreferences } from '@/lib/dtos'
import { LoadingPanel, useSaasDashboard } from '@/lib/use-saas'

const topics=[
  {key:'quota.threshold',label:'Quota thresholds',description:'Warnings when a meter approaches its plan allowance.'},
  {key:'billing.changed',label:'Billing and access',description:'Subscription, payment, trial, and access-mode changes.'},
]

function NotificationsPage(){
  const {data,loading:dashboardLoading}=useSaasDashboard()
  const [items,setItems]=useState<NotificationDelivery[]>([])
  const [preferences,setPreferences]=useState<Record<string,boolean>>({})
  const [loading,setLoading]=useState(true)
  const [saving,setSaving]=useState(false)
  const [notice,setNotice]=useState<string>()
  const prefKey=(topic:string,channel:NotificationChannel)=>`${topic}:${channel}`

  const load=useCallback(async()=>{
    setLoading(true)
    const [inbox,prefs]=await Promise.all([client.api(new QueryNotifications({take:100})),client.api(new GetNotificationPreferences())])
    if(inbox.succeeded)setItems(inbox.response?.results ?? [])
    if(prefs.succeeded){const next:Record<string,boolean>={};for(const p of prefs.response?.results ?? [])if(p.templateKey&&p.channel)next[prefKey(p.templateKey,p.channel)]=p.enabled!==false;setPreferences(next)}
    setLoading(false)
  },[])
  useEffect(()=>{void load()},[load])

  const enabled=(topic:string,channel:NotificationChannel)=>preferences[prefKey(topic,channel)] ?? true
  const toggle=(topic:string,channel:NotificationChannel)=>setPreferences(current=>({...current,[prefKey(topic,channel)]:!enabled(topic,channel)}))
  const save=async()=>{
    setSaving(true);setNotice(undefined)
    const values:NotificationPreferenceInput[]=[]
    for(const topic of topics)for(const channel of [NotificationChannel.InApp,NotificationChannel.Email])values.push(new NotificationPreferenceInput({templateKey:topic.key,channel,enabled:enabled(topic.key,channel)}))
    const api=await client.api(new UpdateNotificationPreferences({preferences:values}))
    setNotice(api.succeeded?'Notification preferences saved.':api.error?.message ?? 'Unable to save preferences.')
    setSaving(false)
  }
  const markRead=async(item:NotificationDelivery)=>{
    if(!item.id||item.readDate)return
    const api=await client.api(new MarkNotificationRead({id:item.id}))
    if(api.succeeded)setItems(current=>current.map(x=>x.id===item.id?{...x,readDate:new Date().toISOString()}:x))
  }

  if(dashboardLoading)return <AppShell><LoadingPanel/></AppShell>
  return <AppShell workspaceName={data?.workspace?.name}>
    <PageHeading eyebrow="Organization updates" title="Notifications" description="See important activity and choose which updates should also arrive by email."/>
    <div className="grid gap-5 xl:grid-cols-[1.15fr_.85fr]">
      <Panel className="overflow-hidden"><div className="flex items-center justify-between border-b border-slate-200 px-6 py-5 dark:border-white/10"><div><h2 className="font-semibold">Inbox</h2><p className="mt-1 text-xs text-slate-400">{items.filter(x=>!x.readDate).length} unread notifications</p></div><Bell className="h-5 w-5 text-[#0b5cff]"/></div>{loading?<div className="grid min-h-64 place-items-center"><span className="h-7 w-7 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]"/></div>:items.length?<div className="divide-y divide-slate-100 dark:divide-white/5">{items.map(item=><button key={item.id} onClick={()=>void markRead(item)} className={`flex w-full gap-4 p-5 text-left transition hover:bg-slate-50 dark:hover:bg-white/[.025] ${!item.readDate?'bg-blue-50/50 dark:bg-blue-400/[.04]':''}`}><span className={`mt-1 h-2.5 w-2.5 shrink-0 rounded-full ${item.readDate?'bg-slate-200 dark:bg-white/10':'bg-[#0b5cff]'}`}/><div className="min-w-0 flex-1"><div className="flex items-start justify-between gap-4"><p className="text-sm font-semibold">{item.subject}</p><span className="shrink-0 text-[10px] text-slate-400">{item.createdDate?new Date(item.createdDate).toLocaleString():''}</span></div><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">{item.body}</p><p className="mt-2 font-mono text-[10px] text-slate-400">{item.templateKey}</p></div>{!item.readDate&&<Check className="mt-0.5 h-4 w-4 shrink-0 text-[#0b5cff]"/>}</button>)}</div>:<div className="grid min-h-64 place-items-center p-8 text-center"><div><Bell className="mx-auto h-7 w-7 text-slate-300"/><h3 className="mt-4 font-semibold">You’re all caught up</h3><p className="mt-2 text-sm text-slate-500">Quota and billing events will appear here.</p></div></div>}</Panel>
      <Panel className="h-fit p-6"><div className="flex items-start gap-3"><span className="grid h-10 w-10 place-items-center rounded-xl bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><SlidersHorizontal className="h-5 w-5"/></span><div><h2 className="font-semibold">Delivery preferences</h2><p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">Critical transactional policy can remain deployment-controlled; these customer choices live in the RDBMS.</p></div></div><div className="mt-6 space-y-4">{topics.map(topic=><div key={topic.key} className="rounded-xl border border-slate-200 p-4 dark:border-white/10"><h3 className="text-sm font-semibold">{topic.label}</h3><p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">{topic.description}</p><div className="mt-4 flex gap-2">{[[NotificationChannel.InApp,Bell,'In app'],[NotificationChannel.Email,Mail,'Email']].map(([channel,Icon,label])=>{const on=enabled(topic.key,channel as NotificationChannel);const ChannelIcon=Icon as typeof Bell;return <button key={channel as string} onClick={()=>toggle(topic.key,channel as NotificationChannel)} className={`inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-xs font-semibold ${on?'border-blue-200 bg-blue-50 text-[#0b5cff] dark:border-blue-400/20 dark:bg-blue-400/10':'border-slate-200 text-slate-400 dark:border-white/10'}`}><ChannelIcon className="h-3.5 w-3.5"/>{label as string}<StatusPill tone={on?'blue':'slate'}>{on?'On':'Off'}</StatusPill></button>})}</div></div>)}</div><div className="mt-5 flex items-center justify-between gap-4"><p className="text-xs text-[#0b5cff]">{notice}</p><button onClick={()=>void save()} disabled={saving} className="shrink-0 rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-50">{saving?'Saving…':'Save preferences'}</button></div></Panel>
    </div>
  </AppShell>
}
export default ValidateAuth(NotificationsPage)
