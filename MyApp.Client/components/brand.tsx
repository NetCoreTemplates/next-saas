'use client'

import { useEffect, useState } from "react"
import { Moon, Sun } from "lucide-react"
import { product } from "@/lib/product"

/** The product mark: a cobalt tile holding a rotated ledger frame around a mint signal. */
export function LogoMark({ size = 'md' }: { size?: 'sm'|'md' }) {
  const box = size === 'sm' ? 'h-8 w-8 rounded-[9px]' : 'h-9 w-9 rounded-[10px]'
  const frame = size === 'sm' ? 'h-[18px] w-[18px]' : 'h-5 w-5'
  return <span aria-hidden className={`relative grid shrink-0 place-items-center overflow-hidden bg-[#0b5cff] shadow-[0_8px_22px_rgba(11,92,255,.28)] ${box}`}>
    <span className={`absolute rotate-45 rounded-[5px] border-2 border-white/90 ${frame}`} />
    <span className="h-1.5 w-1.5 rounded-full bg-[#86efcd]" />
  </span>
}

export function Logo({ size = 'md', className = '' }: { size?: 'sm'|'md', className?: string }) {
  return <span className={`flex items-center gap-3 ${className}`}>
    <LogoMark size={size} />
    <span className="text-[17px] font-bold tracking-[-.03em]">{product.name}</span>
  </span>
}

export function ThemeToggle({ className = '' }: { className?: string }) {
  const [dark, setDark] = useState(false)

  useEffect(() => setDark(document.documentElement.classList.contains('dark')), [])

  const toggle = () => {
    const next = !dark
    setDark(next)
    document.documentElement.classList.toggle('dark', next)
    try { localStorage.setItem('color-scheme', next ? 'dark' : 'light') } catch { /* storage may be unavailable */ }
  }

  return <button type="button" onClick={toggle} aria-label={`Use ${dark ? 'light' : 'dark'} theme`} aria-pressed={dark}
    className={`grid h-9 w-9 place-items-center rounded-[10px] text-slate-500 transition hover:bg-slate-100 hover:text-slate-950 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-white ${className}`}>
    {dark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
  </button>
}
