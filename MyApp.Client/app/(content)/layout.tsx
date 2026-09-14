import type { ReactNode } from 'react'
import Nav from '@/components/nav'
import Footer from '@/components/footer'
import { product } from '@/lib/product'

export default function ContentLayout({ children }: { children: ReactNode }) {
  return <>
    <Nav />
    <div className="relative min-h-[calc(100vh-4.5rem)] overflow-hidden bg-[#f6f8fb] dark:bg-[#07101f]">
      <div className="enterprise-grid pointer-events-none absolute inset-0" />
      <main className="relative mx-auto max-w-5xl px-5 py-14 lg:px-8 lg:py-20">
        <div className="mb-5 flex items-center gap-2 text-xs font-bold uppercase tracking-[.2em] text-[#0b5cff] dark:text-[#86efcd]">
          <span className="h-px w-8 bg-current" />
          {product.name} resources
        </div>
        <article className="surface-shadow rounded-[16px] border border-slate-200 bg-white px-6 py-9 sm:px-10 sm:py-12 lg:px-14 dark:border-white/10 dark:bg-[#0c1729]">
          <div className="prose prose-slate max-w-none prose-headings:tracking-[-.035em] prose-headings:text-slate-950 prose-h1:text-4xl prose-h1:font-semibold prose-h2:mt-10 prose-h2:text-2xl prose-a:font-semibold prose-a:text-[#0b5cff] prose-strong:text-slate-900 prose-li:marker:text-[#0b5cff] dark:prose-invert dark:prose-headings:text-white dark:prose-a:text-[#86efcd] dark:prose-strong:text-white">
            {children}
          </div>
        </article>
      </main>
    </div>
    <Footer />
  </>
}
