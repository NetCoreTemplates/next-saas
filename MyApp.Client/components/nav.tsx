'use client'

import Link from "next/link"
import { usePathname } from "next/navigation"
import { useEffect, useState } from "react"
import { appAuth } from "@/lib/auth"
import { ArrowRight, Moon, Sun } from "lucide-react"
import { product } from "@/lib/product"

type NavItem = {
    href?:string,
    name:string,
    type?:string,
    show?:string,
    hide?:string,
    onClick?:() => void
}

function ThemeToggle() {
    const [dark, setDark] = useState(false)

    useEffect(() => setDark(document.documentElement.classList.contains('dark')), [])

    const toggle = () => {
        const next = !dark
        setDark(next)
        document.documentElement.classList.toggle('dark', next)
        localStorage.setItem('color-scheme', next ? 'dark' : 'light')
    }

    return <button type="button" onClick={toggle} aria-label={`Use ${dark ? 'light' : 'dark'} theme`} aria-pressed={dark}
        className="grid h-9 w-9 place-items-center rounded-[10px] text-slate-500 transition hover:bg-slate-100 hover:text-slate-950 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-white">
        {dark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
    </button>
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
            <Link href="/" className="group flex items-center gap-3" aria-label={`${product.name} home`}>
                <span className="relative grid h-9 w-9 place-items-center overflow-hidden rounded-[10px] bg-[#0b5cff] shadow-[0_8px_22px_rgba(11,92,255,.28)]">
                    <span className="absolute h-5 w-5 rotate-45 rounded-[5px] border-2 border-white/90" />
                    <span className="h-1.5 w-1.5 rounded-full bg-[#86efcd]" />
                </span>
                <span className="text-[17px] font-bold tracking-[-.03em] text-slate-950 dark:text-white">{product.name}</span>
            </Link>
            <nav className="ml-12 hidden flex-1 lg:block" aria-label="Primary navigation">
                <ul className="flex items-center gap-1">
                    {items.map(x => {
                        const isActive = pathname === x.href || pathname.startsWith(x.href + '/')
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
    </header>)
}
