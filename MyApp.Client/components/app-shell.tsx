'use client'

import Link from "next/link"
import { usePathname } from "next/navigation"
import { useEffect, useState } from "react"
import { appAuth } from "@/lib/auth"
import { client } from "@/lib/gateway"
import { GetMyWorkspaces, SwitchWorkspace, WorkspaceAccessInfo } from "@/lib/dtos"
import { Activity, BarChart3, Bell, Building2, ChevronsUpDown, CreditCard, FileText, Gauge, KeyRound, LayoutDashboard, LifeBuoy, LogOut, ScrollText, Settings, ShieldCheck, SlidersHorizontal, Users } from "lucide-react"
import { product, productInitial } from "@/lib/product"

const links = [
  { href:'/dashboard', label:'Overview', icon:LayoutDashboard },
  { href:'/documents', label:'Documents', icon:FileText },
  { href:'/usage', label:'Usage', icon:BarChart3 },
  { href:'/notifications', label:'Notifications', icon:Bell },
  { href:'/audit', label:'Audit log', icon:ScrollText },
  { href:'/billing', label:'Plans & billing', icon:CreditCard },
  { href:'/api-keys', label:'API keys', icon:KeyRound },
  { href:'/team', label:'Team', icon:Users },
  { href:'/settings', label:'Settings', icon:Settings },
]

const adminLinks = [
  { href:'/admin', label:'Overview', icon:LayoutDashboard, roles:['Admin','Support','BillingAdmin'] },
  { href:'/admin/customers', label:'Customers', icon:Users, roles:['Admin','Support','BillingAdmin'] },
  { href:'/admin/plans', label:'Plans & billing', icon:CreditCard, roles:['Admin'] },
  { href:'/admin/usage', label:'Usage & analytics', icon:BarChart3, roles:['Admin'] },
  { href:'/admin/operations', label:'Operations', icon:Activity, roles:['Admin','BillingAdmin'] },
  { href:'/admin/security', label:'Security & data', icon:ShieldCheck, roles:['Admin','Support'] },
  { href:'/admin/settings', label:'Settings', icon:SlidersHorizontal, roles:['Admin'] },
]

export default function AppShell({ children, workspaceName = 'My organization' }: { children: React.ReactNode, workspaceName?: string }) {
  const pathname = usePathname()
  const { user, hasRole, signOut } = appAuth()
  const inOperationsCenter = pathname.startsWith('/admin')
  const visibleAdminLinks = adminLinks.filter(item => item.roles.some(hasRole))
  const [workspaces,setWorkspaces] = useState<WorkspaceAccessInfo[]>([])
  const [switching,setSwitching] = useState(false)
  useEffect(()=>{if(!user?.userId)return;client.api(new GetMyWorkspaces()).then(api=>{if(api.succeeded)setWorkspaces(api.response?.results??[])})},[user?.userId])
  const switchWorkspace=async(workspaceId:string)=>{if(!workspaceId||workspaces.some(x=>x.workspace?.id===workspaceId&&x.isActive))return;setSwitching(true);const api=await client.api(new SwitchWorkspace({workspaceId}));if(api.succeeded)window.location.reload();else setSwitching(false)}
  const activeWorkspace=workspaces.find(x=>x.isActive)
  return <div className="min-h-screen bg-[#f5f7fa] text-slate-900 dark:bg-[#07101f] dark:text-white">
    <aside className="fixed inset-y-0 left-0 z-40 hidden w-[260px] flex-col border-r border-slate-200 bg-[#091426] text-white lg:flex">
      <Link href="/" className="flex h-18 items-center gap-3 border-b border-white/10 px-6"><span className="relative grid h-9 w-9 place-items-center rounded-[10px] bg-[#0b5cff]"><span className="h-4 w-4 rotate-45 rounded-[4px] border-2 border-white"/></span><span className="font-bold tracking-[-.03em]">{product.name}</span></Link>
      {inOperationsCenter
        ? <div className="mx-4 mt-5 rounded-xl border border-blue-300/15 bg-gradient-to-br from-blue-500/15 to-emerald-300/[.06] p-4"><div className="flex items-center gap-3"><span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-[#0b5cff] shadow-[0_8px_24px_rgba(11,92,255,.28)]"><Gauge className="h-4 w-4"/></span><div><p className="text-sm font-semibold">Operations Center</p><p className="mt-0.5 text-[10px] text-slate-400">Platform administration</p></div></div><Link href="/dashboard" className="mt-3 block border-t border-white/10 pt-3 text-[11px] font-semibold text-slate-300 transition hover:text-white">← Return to organization</Link></div>
        : <div className="mx-4 mt-5 rounded-xl border border-white/10 bg-white/[.045] p-3"><div className="flex items-center gap-3"><span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-white/10"><Building2 className="h-4 w-4 text-[#86efcd]"/></span><label className="min-w-0 flex-1"><span className="sr-only">Active organization</span><select aria-label="Active organization" value={activeWorkspace?.workspace?.id??''} disabled={switching||workspaces.length<2} onChange={e=>void switchWorkspace(e.target.value)} className="block w-full truncate border-0 bg-transparent p-0 pr-5 text-sm font-semibold text-white focus:ring-0 disabled:appearance-none disabled:opacity-100"><option value="">{workspaceName}</option>{workspaces.map(x=><option key={x.workspace?.id} value={x.workspace?.id} className="text-slate-900">{x.workspace?.name}</option>)}</select><span className="mt-0.5 block truncate text-[10px] text-slate-400">{switching?'Switching…':workspaces.length>1?`${workspaces.length} organizations`:activeWorkspace?.role??'Organization'}</span></label>{workspaces.length>1&&<ChevronsUpDown className="h-4 w-4 shrink-0 text-slate-500"/>}</div><Link href="/settings#create-organization" className="mt-3 block border-t border-white/10 pt-2.5 text-center text-[11px] font-semibold text-slate-400 transition hover:text-white">+ New organization</Link></div>}
      <nav className="mt-6 flex-1 overflow-y-auto px-3">{inOperationsCenter ? <><p className="px-3 text-[10px] font-bold uppercase tracking-[.18em] text-slate-500">Platform</p><div className="mt-3 space-y-1">{visibleAdminLinks.map(({href,label,icon:Icon})=>{const active=pathname===href;return <Link key={href} href={href} className={`flex items-center gap-3 rounded-[9px] px-3 py-2.5 text-sm font-medium transition ${active?'bg-[#0b5cff] text-white shadow-[0_8px_20px_rgba(11,92,255,.2)]':'text-slate-300 hover:bg-white/[.06] hover:text-white'}`}><Icon className="h-[17px] w-[17px]"/>{label}</Link>})}</div></> : <><p className="px-3 text-[10px] font-bold uppercase tracking-[.18em] text-slate-500">Organization</p><div className="mt-3 space-y-1">{links.map(({href,label,icon:Icon}) => { const active=pathname===href; return <Link key={href} href={href} className={`flex items-center gap-3 rounded-[9px] px-3 py-2.5 text-sm font-medium transition ${active ? 'bg-[#0b5cff] text-white shadow-[0_8px_20px_rgba(11,92,255,.2)]' : 'text-slate-300 hover:bg-white/[.06] hover:text-white'}`}><Icon className="h-[17px] w-[17px]"/>{label}</Link>})}</div>{['Admin','Support','BillingAdmin'].some(hasRole) && <><p className="mt-7 px-3 text-[10px] font-bold uppercase tracking-[.18em] text-slate-500">Operations</p><Link href="/admin" className="mt-3 flex items-center gap-3 rounded-[9px] px-3 py-2.5 text-sm font-medium text-slate-300 transition hover:bg-white/[.06] hover:text-white"><ShieldCheck className="h-[17px] w-[17px]"/>Operations center</Link></>}</>}</nav>
      <div className="border-t border-white/10 p-4"><a href={`mailto:${product.supportEmail}`} className="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-slate-400 hover:bg-white/5 hover:text-white"><LifeBuoy className="h-4 w-4"/>Help & support</a></div>
    </aside>
    <div className="lg:pl-[260px]">
      <header className="sticky top-0 z-30 flex h-18 items-center border-b border-slate-200 bg-white/90 px-5 backdrop-blur-xl dark:border-white/10 dark:bg-[#0a1424]/90 lg:px-8"><div className="flex items-center gap-3 lg:hidden"><span className="grid h-8 w-8 place-items-center rounded-lg bg-[#0b5cff] font-bold text-white">{productInitial}</span><span className="font-bold">{product.name}</span></div><div className="ml-auto flex items-center gap-3"><div className="hidden text-right sm:block"><p className="text-xs font-semibold text-slate-800 dark:text-slate-100">{user?.displayName || user?.userName}</p><p className="text-[10px] text-slate-400">{hasRole('Admin')?'Administrator':hasRole('BillingAdmin')?'Billing operator':hasRole('Support')?'Support operator':'Organization member'}</p></div>{user?.profileUrl && <img src={user.profileUrl} alt="" className="h-9 w-9 rounded-[10px] bg-slate-100"/>}<button onClick={() => signOut('/')} title="Sign out" className="grid h-9 w-9 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-800 dark:hover:bg-white/5 dark:hover:text-white"><LogOut className="h-4 w-4"/></button></div></header>
      <main className="mx-auto max-w-[1500px] p-5 pb-24 lg:p-8">{children}</main>
    </div>
    <nav className="fixed inset-x-3 bottom-3 z-50 flex items-center gap-1 overflow-x-auto rounded-[14px] border border-white/10 bg-[#091426]/95 p-2 text-white shadow-2xl backdrop-blur-xl lg:hidden" aria-label={inOperationsCenter?'Operations Center navigation':'Organization navigation'}>{(inOperationsCenter?visibleAdminLinks:links.filter(x=>['/dashboard','/documents','/usage','/billing','/settings'].includes(x.href))).map(({href,label,icon:Icon})=><Link key={href} href={href} className={`flex min-w-[4.25rem] flex-1 flex-col items-center gap-1 rounded-lg px-2 py-1.5 text-center text-[9px] ${pathname===href?'bg-[#0b5cff] text-white':'text-slate-400'}`}><Icon className="h-4 w-4"/><span className="whitespace-nowrap">{label.replace('Plans & ','').replace(' & analytics','').replace(' & data','')}</span></Link>)}</nav>
  </div>
}

export function PageHeading({ eyebrow, title, description, action }: { eyebrow?:string, title:string, description?:string, action?:React.ReactNode }) {
  return <div className="mb-8 flex flex-col gap-5 border-b border-slate-200 pb-7 sm:flex-row sm:items-end sm:justify-between dark:border-white/10"><div>{eyebrow && <p className="mb-2 text-[10px] font-bold uppercase tracking-[.18em] text-[#0b5cff] dark:text-[#86efcd]">{eyebrow}</p>}<h1 className="text-3xl font-semibold tracking-[-.045em] text-slate-950 dark:text-white">{title}</h1>{description && <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-500 dark:text-slate-400">{description}</p>}</div>{action}</div>
}

export function Panel({ children, className='' }: {children:React.ReactNode,className?:string}) { return <div className={`rounded-[14px] border border-slate-200 bg-white shadow-[0_1px_2px_rgba(16,24,40,.03)] dark:border-white/10 dark:bg-white/[.035] ${className}`}>{children}</div> }

export function StatusPill({ children, tone='green' }: {children:React.ReactNode,tone?:'green'|'blue'|'amber'|'slate'}) { const colors={green:'bg-emerald-50 text-emerald-700 dark:bg-emerald-400/10 dark:text-[#86efcd]',blue:'bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10 dark:text-blue-300',amber:'bg-amber-50 text-amber-700 dark:bg-amber-400/10 dark:text-amber-300',slate:'bg-slate-100 text-slate-600 dark:bg-white/5 dark:text-slate-300'}; return <span className={`inline-flex rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-[.08em] ${colors[tone]}`}>{children}</span> }
