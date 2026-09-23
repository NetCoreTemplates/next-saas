'use client'

import Link from "next/link"
import { usePathname } from "next/navigation"
import { appAuth } from "@/lib/auth"
import { ArrowRight } from "lucide-react"
import { Logo, ThemeToggle } from "./brand"
import { product } from "@/lib/product"

type NavItem = {
    href?:string,
    name:string,
    type?:string,
    show?:string,
    hide?:string,
    onClick?:() => void
}

export default function Nav() {
    const pathname = usePathname()

    const items:NavItem[] = [
        { href: '/#platform', name: 'Product'},
        { href: '/#solutions', name: 'How it works'},
        { href: '/pricing', name: 'Pricing'},
    ]

    const { user, signOut } = appAuth()

    return (<header className="sticky top-0 z-50 border-b border-slate-200/80 bg-white/88 backdrop-blur-xl dark:border-white/10 dark:bg-[#07101f]/88">
        <div className="mx-auto flex h-18 max-w-7xl items-center px-5 lg:px-8">
            <Link href="/" aria-label={`${product.name} home`} className="rounded-[10px] text-slate-950 dark:text-white">
                <Logo />
            </Link>
            <nav className="ml-12 hidden flex-1 lg:block" aria-label="Primary navigation">
                <ul className="flex items-center gap-1">
                    {items.map(x => {
                        const isActive = !x.href!.includes('#') && (pathname === x.href || pathname.startsWith(x.href + '/'))
                        return (<li key={x.name}>
                            <Link href={x.href!} className={`inline-flex items-center gap-1 rounded-lg px-3.5 py-2 text-sm font-medium transition-colors hover:bg-slate-100 hover:text-slate-950 dark:hover:bg-white/5 dark:hover:text-white ${isActive ? 'text-[#0b5cff]' : 'text-slate-600 dark:text-slate-300'}`}>
                                {x.name}
                            </Link>
                        </li>)
                    })}
                </ul>
            </nav>
            <div className="ml-auto flex items-center gap-2">
                <ThemeToggle />
                {user ? <>
                    <Link href="/dashboard" className="hidden rounded-lg px-3 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-100 sm:inline-flex dark:text-slate-200 dark:hover:bg-white/5">Organization</Link>
                    <button onClick={() => signOut()} className="hidden rounded-lg px-3 py-2 text-sm text-slate-500 hover:text-slate-950 md:block dark:text-slate-400 dark:hover:text-white">Sign out</button>
                </> : <Link href="/signin" className="hidden rounded-lg px-3 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-100 sm:inline-flex dark:text-slate-200 dark:hover:bg-white/5">Sign in</Link>}
                <Link href={user ? '/dashboard' : '/signup'} className="inline-flex items-center gap-2 rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white shadow-[0_8px_20px_rgba(11,92,255,.22)] transition hover:-translate-y-px hover:bg-[#084dcc]">
                    {user ? 'Open app' : 'Start free'} <ArrowRight className="h-4 w-4" />
                </Link>
            </div>
        </div>
        <nav className="border-t border-slate-200/80 lg:hidden dark:border-white/10" aria-label="Primary navigation">
            <ul className="mx-auto flex max-w-7xl items-center gap-1 overflow-x-auto px-3 py-1.5 text-sm font-medium">
                {items.map(x => <li key={x.name}><Link href={x.href!} className={`block whitespace-nowrap rounded-lg px-3 py-1.5 ${pathname === x.href ? 'text-[#0b5cff]' : 'text-slate-600 dark:text-slate-300'}`}>{x.name}</Link></li>)}
                {!user && <li className="ml-auto sm:hidden"><Link href="/signin" className="block whitespace-nowrap rounded-lg px-3 py-1.5 font-semibold text-slate-800 dark:text-white">Sign in</Link></li>}
            </ul>
        </nav>
    </header>)
}
