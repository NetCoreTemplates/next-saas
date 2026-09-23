'use client'

import Link from "next/link"
import { usePathname } from "next/navigation"
import { useEffect, useState } from "react"
import { appAuth } from "@/lib/auth"
import { client } from "@/lib/gateway"
import { GetMyWorkspaces, SwitchWorkspace, WorkspaceAccessInfo } from "@/lib/dtos"
import { Activity, BarChart3, Bell, Building2, ChevronsUpDown, CreditCard, FileText, Gauge, KeyRound, LayoutDashboard, LifeBuoy, LogOut, Menu, ScrollText, Settings, ShieldCheck, SlidersHorizontal, Users, X } from "lucide-react"
import { product } from "@/lib/product"
import { Logo, LogoMark, ThemeToggle } from "./brand"

type NavLink = { href:string, label:string, short?:string, icon:typeof LayoutDashboard, roles?:string[] }

const allLinks: NavLink[] = [
  { href:'/dashboard', label:'Overview', icon:LayoutDashboard },
  { href:'/documents', label:'Documents', icon:FileText },
  { href:'/usage', label:'Usage', icon:BarChart3 },
  { href:'/notifications', label:'Notifications', icon:Bell },
  { href:'/audit', label:'Audit log', icon:ScrollText },
  { href:'/billing', label:'Plans & billing', short:'Billing', icon:CreditCard },
  { href:'/api-keys', label:'API keys', icon:KeyRound },
  { href:'/team', label:'Team', icon:Users },
  { href:'/settings', label:'Settings', icon:Settings },
]

const adminLinks: NavLink[] = [
  { href:'/admin', label:'Overview', icon:LayoutDashboard, roles:['Admin','Support','BillingAdmin'] },
  { href:'/admin/customers', label:'Customers', icon:Users, roles:['Admin','Support','BillingAdmin'] },
  { href:'/admin/plans', label:'Plans & billing', short:'Plans', icon:CreditCard, roles:['Admin'] },
  { href:'/admin/usage', label:'Usage & analytics', short:'Usage', icon:BarChart3, roles:['Admin'] },
  { href:'/admin/operations', label:'Operations', icon:Activity, roles:['Admin','BillingAdmin'] },
  { href:'/admin/security', label:'Security & data', short:'Security', icon:ShieldCheck, roles:['Admin','Support'] },
  { href:'/admin/settings', label:'Settings', icon:SlidersHorizontal, roles:['Admin'] },
]

/** Links rendered in the mobile tab bar; everything else lives behind "More". */
const mobileTabs = ['/dashboard','/documents','/usage','/billing']
const operatorRoles = ['Admin','Support','BillingAdmin']

// Section roots only match themselves; everything else also owns its nested routes (e.g. /team/invite).
const isActive = (pathname:string, href:string) => pathname === href || (href !== '/admin' && pathname.startsWith(href + '/'))

const sectionLabel = 'text-[10px] font-bold uppercase tracking-[.18em] text-slate-500'

function NavItem({ link, active, onNavigate }: { link:NavLink, active:boolean, onNavigate?:() => void }) {
  const Icon = link.icon
  return <Link href={link.href} onClick={onNavigate} aria-current={active ? 'page' : undefined}
    className={`flex items-center gap-3 rounded-[9px] px-3 py-2.5 text-sm font-medium transition ${active ? 'bg-[#0b5cff] text-white shadow-[0_8px_20px_rgba(11,92,255,.2)]' : 'text-slate-300 hover:bg-white/[.06] hover:text-white'}`}>
    <Icon className="h-[17px] w-[17px]"/>{link.label}
  </Link>
}

function WorkspaceSwitcher({ workspaces, workspaceName, switching, onSwitch }: { workspaces:WorkspaceAccessInfo[], workspaceName:string, switching:boolean, onSwitch:(id:string) => void }) {
  const active = workspaces.find(x => x.isActive)
  return <div className="rounded-xl border border-white/10 bg-white/[.045] p-3">
    <div className="flex items-center gap-3">
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-white/10"><Building2 className="h-4 w-4 text-[#86efcd]"/></span>
      <label className="min-w-0 flex-1">
        <span className="sr-only">Active organization</span>
        <select aria-label="Active organization" value={active?.workspace?.id ?? ''} disabled={switching || workspaces.length < 2} onChange={e => onSwitch(e.target.value)}
          className="block w-full truncate border-0 bg-transparent p-0 pr-5 text-sm font-semibold text-white focus:ring-0 disabled:appearance-none disabled:opacity-100">
          <option value="">{workspaceName}</option>
          {workspaces.map(x => <option key={x.workspace?.id} value={x.workspace?.id} className="text-slate-900">{x.workspace?.name}</option>)}
        </select>
        <span className="mt-0.5 block truncate text-[10px] text-slate-400">{switching ? 'Switching…' : workspaces.length > 1 ? `${workspaces.length} organizations` : active?.role ?? 'Organization'}</span>
      </label>
      {workspaces.length > 1 && <ChevronsUpDown className="h-4 w-4 shrink-0 text-slate-500"/>}
    </div>
    <Link href="/settings#create-organization" className="mt-3 block border-t border-white/10 pt-2.5 text-center text-[11px] font-semibold text-slate-400 transition hover:text-white">+ New organization</Link>
  </div>
}

function OperationsCard() {
  return <div className="rounded-xl border border-blue-300/15 bg-gradient-to-br from-blue-500/15 to-emerald-300/[.06] p-4">
    <div className="flex items-center gap-3">
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-[#0b5cff] shadow-[0_8px_24px_rgba(11,92,255,.28)]"><Gauge className="h-4 w-4"/></span>
      <div><p className="text-sm font-semibold">Operations Center</p><p className="mt-0.5 text-[10px] text-slate-400">Platform administration</p></div>
    </div>
    <Link href="/dashboard" className="mt-3 block border-t border-white/10 pt-3 text-[11px] font-semibold text-slate-300 transition hover:text-white">← Return to organization</Link>
  </div>
}

export default function AppShell({ children, workspaceName = 'My organization' }: { children: React.ReactNode, workspaceName?: string }) {
  const pathname = usePathname()
  const { user, hasRole, signOut } = appAuth()
  const inOperationsCenter = pathname.startsWith('/admin')
  const visibleAdminLinks = adminLinks.filter(item => item.roles!.some(hasRole))
  const isOperator = operatorRoles.some(hasRole)
  const [workspaces,setWorkspaces] = useState<WorkspaceAccessInfo[]>([])
  const [switching,setSwitching] = useState(false)
  const [menuOpen,setMenuOpen] = useState(false)

  useEffect(() => {
    if (!user?.userId) return
    client.api(new GetMyWorkspaces()).then(api => { if (api.succeeded) setWorkspaces(api.response?.results ?? []) })
  }, [user?.userId])

  useEffect(() => setMenuOpen(false), [pathname])
  useEffect(() => {
    if (!menuOpen) return
    const close = (e:KeyboardEvent) => { if (e.key === 'Escape') setMenuOpen(false) }
    document.addEventListener('keydown', close)
    document.body.style.overflow = 'hidden'
    return () => { document.removeEventListener('keydown', close); document.body.style.overflow = '' }
  }, [menuOpen])

  const switchWorkspace = async (workspaceId:string) => {
    if (!workspaceId || workspaces.some(x => x.workspace?.id === workspaceId && x.isActive)) return
    setSwitching(true)
    const api = await client.api(new SwitchWorkspace({ workspaceId }))
    if (api.succeeded) window.location.reload()
    else setSwitching(false)
  }

  const activeWorkspace = workspaces.find(x => x.isActive)
  const individual = activeWorkspace?.workspace?.kind === 'Individual'
  const links = individual ? allLinks.filter(x => x.href !== '/team') : allLinks
  const sectionLinks = inOperationsCenter ? visibleAdminLinks : links
  const current = sectionLinks.find(x => isActive(pathname, x.href))
  const contextName = inOperationsCenter ? 'Operations Center' : activeWorkspace?.workspace?.name ?? workspaceName
  const roleLabel = hasRole('Admin') ? 'Administrator' : hasRole('BillingAdmin') ? 'Billing operator' : hasRole('Support') ? 'Support operator' : individual ? 'Personal account' : 'Organization member'
  const tabs = inOperationsCenter ? visibleAdminLinks.slice(0, 4) : links.filter(x => mobileTabs.includes(x.href))

  const navigation = (onNavigate?:() => void) => inOperationsCenter
    ? <><p className={`px-3 ${sectionLabel}`}>Platform</p><div className="mt-3 space-y-1">{visibleAdminLinks.map(link => <NavItem key={link.href} link={link} active={isActive(pathname, link.href)} onNavigate={onNavigate}/>)}</div></>
    : <>
      <p className={`px-3 ${sectionLabel}`}>{individual ? 'Account' : 'Organization'}</p>
      <div className="mt-3 space-y-1">{links.map(link => <NavItem key={link.href} link={link} active={isActive(pathname, link.href)} onNavigate={onNavigate}/>)}</div>
      {isOperator && <><p className={`mt-7 px-3 ${sectionLabel}`}>Operations</p><div className="mt-3"><NavItem link={{ href:'/admin', label:'Operations center', icon:ShieldCheck }} active={false} onNavigate={onNavigate}/></div></>}
    </>

  return <div className="min-h-screen bg-[#f5f7fa] text-slate-900 dark:bg-[#07101f] dark:text-white">
    <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-[60] focus:rounded-lg focus:bg-[#0b5cff] focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-white">Skip to content</a>
    <aside className="fixed inset-y-0 left-0 z-40 hidden w-[260px] flex-col border-r border-slate-200 bg-[#091426] text-white lg:flex dark:border-white/10">
      <Link href="/" className="flex h-18 items-center border-b border-white/10 px-6" aria-label={`${product.name} home`}><Logo/></Link>
      <div className="mx-4 mt-5">{inOperationsCenter ? <OperationsCard/> : <WorkspaceSwitcher workspaces={workspaces} workspaceName={workspaceName} switching={switching} onSwitch={id => void switchWorkspace(id)}/>}</div>
      <nav className="mt-6 flex-1 overflow-y-auto px-3" aria-label={inOperationsCenter ? 'Operations Center' : 'Organization'}>{navigation()}</nav>
      <div className="border-t border-white/10 p-4"><a href={`mailto:${product.supportEmail}`} className="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-slate-400 hover:bg-white/5 hover:text-white"><LifeBuoy className="h-4 w-4"/>Help & support</a></div>
    </aside>

    <div className="lg:pl-[260px]">
      <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b border-slate-200 bg-white/90 px-4 backdrop-blur-xl sm:px-5 lg:h-18 lg:px-8 dark:border-white/10 dark:bg-[#0a1424]/90">
        <Link href="/" className="lg:hidden" aria-label={`${product.name} home`}><LogoMark size="sm"/></Link>
        <nav aria-label="Breadcrumb" className="min-w-0 text-sm">
          <ol className="flex items-center gap-2 text-slate-400">
            <li className="truncate font-semibold text-slate-800 dark:text-slate-100">{contextName}</li>
            {current && <><li aria-hidden className="hidden text-slate-300 sm:block dark:text-slate-600">/</li><li aria-current="page" className="hidden truncate sm:block">{current.label}</li></>}
          </ol>
        </nav>
        <div className="ml-auto flex items-center gap-1.5 sm:gap-3">
          <ThemeToggle/>
          <div className="hidden text-right sm:block"><p className="text-xs font-semibold text-slate-800 dark:text-slate-100">{user?.displayName || user?.userName}</p><p className="text-[10px] text-slate-400">{roleLabel}</p></div>
          {user?.profileUrl && <img src={user.profileUrl} alt="" className="h-9 w-9 rounded-[10px] bg-slate-100"/>}
          <button onClick={() => signOut('/')} title="Sign out" aria-label="Sign out" className="hidden h-9 w-9 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-800 sm:grid dark:hover:bg-white/5 dark:hover:text-white"><LogOut className="h-4 w-4"/></button>
        </div>
      </header>
      <main id="main" tabIndex={-1} className="mx-auto max-w-[1500px] p-4 pb-28 outline-none sm:p-5 sm:pb-28 lg:p-8">{children}</main>
    </div>

    <nav className="fixed inset-x-3 bottom-3 z-50 flex items-center gap-1 rounded-[14px] border border-white/10 bg-[#091426]/95 p-1.5 text-white shadow-2xl backdrop-blur-xl lg:hidden" aria-label={inOperationsCenter ? 'Operations Center navigation' : 'Organization navigation'}>
      {tabs.map(({ href, label, short, icon:Icon }) => { const active = isActive(pathname, href); return <Link key={href} href={href} aria-current={active ? 'page' : undefined} className={`flex min-w-0 flex-1 flex-col items-center gap-1 rounded-lg px-1 py-1.5 text-[10px] font-medium ${active ? 'bg-[#0b5cff] text-white' : 'text-slate-400'}`}><Icon className="h-[18px] w-[18px]"/><span className="truncate">{short ?? label}</span></Link> })}
      <button type="button" onClick={() => setMenuOpen(true)} aria-expanded={menuOpen} aria-controls="mobile-menu" className={`flex min-w-0 flex-1 flex-col items-center gap-1 rounded-lg px-1 py-1.5 text-[10px] font-medium ${!tabs.some(x => isActive(pathname, x.href)) ? 'bg-white/10 text-white' : 'text-slate-400'}`}><Menu className="h-[18px] w-[18px]"/><span>More</span></button>
    </nav>

    {menuOpen && <div className="fixed inset-0 z-[55] lg:hidden" role="dialog" aria-modal="true" aria-label="Navigation menu" id="mobile-menu">
      <button type="button" aria-label="Close menu" onClick={() => setMenuOpen(false)} className="animate-fade absolute inset-0 bg-[#030814]/60 backdrop-blur-sm"/>
      <div className="animate-sheet absolute inset-x-0 bottom-0 max-h-[85vh] overflow-y-auto rounded-t-[20px] border-t border-white/10 bg-[#091426] px-4 pb-8 pt-3 text-white shadow-2xl">
        <div className="mx-auto mb-4 h-1 w-10 rounded-full bg-white/15"/>
        <div className="mb-5 flex items-center justify-between">
          <div className="min-w-0"><p className="truncate text-sm font-semibold">{user?.displayName || user?.userName}</p><p className="text-[11px] text-slate-400">{roleLabel}</p></div>
          <button type="button" onClick={() => setMenuOpen(false)} aria-label="Close menu" className="grid h-9 w-9 place-items-center rounded-lg text-slate-400 hover:bg-white/5 hover:text-white"><X className="h-5 w-5"/></button>
        </div>
        {inOperationsCenter ? <OperationsCard/> : <WorkspaceSwitcher workspaces={workspaces} workspaceName={workspaceName} switching={switching} onSwitch={id => void switchWorkspace(id)}/>}
        <nav className="mt-6" aria-label="All pages">{navigation(() => setMenuOpen(false))}</nav>
        <div className="mt-6 grid grid-cols-2 gap-2 border-t border-white/10 pt-4">
          <a href={`mailto:${product.supportEmail}`} className="flex items-center justify-center gap-2 rounded-lg bg-white/5 px-3 py-2.5 text-sm text-slate-300"><LifeBuoy className="h-4 w-4"/>Support</a>
          <button type="button" onClick={() => signOut('/')} className="flex items-center justify-center gap-2 rounded-lg bg-white/5 px-3 py-2.5 text-sm text-slate-300"><LogOut className="h-4 w-4"/>Sign out</button>
        </div>
      </div>
    </div>}
  </div>
}

export function PageHeading({ eyebrow, title, description, action }: { eyebrow?:string, title:string, description?:string, action?:React.ReactNode }) {
  return <div className="mb-6 flex flex-col gap-5 border-b border-slate-200 pb-6 sm:mb-8 sm:flex-row sm:items-end sm:justify-between sm:pb-7 dark:border-white/10"><div>{eyebrow && <p className="mb-2 text-[10px] font-bold uppercase tracking-[.18em] text-[#0b5cff] dark:text-[#86efcd]">{eyebrow}</p>}<h1 className="text-[1.75rem] font-semibold tracking-[-.045em] text-slate-950 sm:text-3xl dark:text-white">{title}</h1>{description && <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-500 dark:text-slate-400">{description}</p>}</div>{action}</div>
}

export function Panel({ children, className='' }: {children:React.ReactNode,className?:string}) { return <div className={`rounded-[14px] border border-slate-200 bg-white shadow-[0_1px_2px_rgba(16,24,40,.03)] dark:border-white/10 dark:bg-white/[.035] ${className}`}>{children}</div> }

export type Tone = 'green'|'blue'|'amber'|'red'|'slate'

export function StatusPill({ children, tone='green' }: {children:React.ReactNode,tone?:Tone}) { const colors={green:'bg-emerald-50 text-emerald-700 dark:bg-emerald-400/10 dark:text-[#86efcd]',blue:'bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10 dark:text-blue-300',amber:'bg-amber-50 text-amber-700 dark:bg-amber-400/10 dark:text-amber-300',red:'bg-red-50 text-red-700 dark:bg-red-400/10 dark:text-red-300',slate:'bg-slate-100 text-slate-600 dark:bg-white/5 dark:text-slate-300'}; return <span className={`inline-flex whitespace-nowrap rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-[.08em] ${colors[tone]}`}>{children}</span> }

/** Allowance consumption tone: healthy below 80%, approaching at 80–99%, exhausted at 100%. */
export const quotaTone = (percent?:number): Tone => (percent ?? 0) >= 100 ? 'red' : (percent ?? 0) >= 80 ? 'amber' : 'green'
export const quotaText = { green:'text-[#0b5cff] dark:text-blue-300', amber:'text-amber-600 dark:text-amber-300', red:'text-red-600 dark:text-red-300' } as Record<Tone,string>

/** A quota bar colored by consumption. Pass `tone` for progress that is not an allowance, such as time elapsed. */
export function Meter({ percent, label, tone, className='h-2' }: { percent?:number, label?:string, tone?:'green'|'amber'|'red', className?:string }) {
  const value = Math.min(100, Math.max(0, percent ?? 0))
  const resolved = tone ?? quotaTone(percent)
  const fill = resolved === 'red' ? 'bg-red-500' : resolved === 'amber' ? 'bg-gradient-to-r from-amber-400 to-amber-500' : 'bg-gradient-to-r from-[#0b5cff] to-[#86efcd]'
  return <div role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(value)} className={`overflow-hidden rounded-full bg-slate-100 dark:bg-white/10 ${className}`}>
    <div className={`meter-fill h-full rounded-full ${fill}`} style={{ width:`${value}%` }}/>
  </div>
}
