import Link from "next/link"
import {
  ArrowRight, BarChart3, Bot, Check, ChevronRight, Database,
  FileSearch, Files, Globe2, LockKeyhole, Search, Sparkles,
} from "lucide-react"
import { product } from "@/lib/product"

const Feature = ({ icon, title, children }: { icon: React.ReactNode, title: string, children: React.ReactNode }) => <div className="group border-t border-slate-200 pt-6 dark:border-white/10">
  <div className="mb-5 grid h-10 w-10 place-items-center rounded-xl border border-slate-200 bg-white text-[#0b5cff] shadow-sm transition group-hover:-translate-y-1 group-hover:border-blue-200 dark:border-white/10 dark:bg-white/5 dark:text-[#86efcd]">{icon}</div>
  <h3 className="text-lg font-semibold tracking-[-.02em] text-slate-950 dark:text-white">{title}</h3>
  <p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">{children}</p>
</div>

const sources = [
  { n: 1, title: 'Data retention policy', path: 'policies/retention.pdf', section: '§4.2 Deleted content', score: '0.94' },
  { n: 2, title: 'Administrator guide', path: 'guides/admin/exports.md', section: 'Export expiry', score: '0.91' },
]

const delay = (d: string) => ({ '--d': d }) as React.CSSProperties

const Cite = ({ n, d }: { n: number, d: string }) => <a href={`#source-${n}`} className="cite-link mx-0.5 inline-flex align-[1px]" aria-label={`Source ${n}`}>
  <span className="cite-marker cite-pulse grid h-[18px] min-w-[18px] place-items-center rounded-[5px] border border-[#86efcd]/40 bg-[#86efcd]/10 px-1 font-mono text-[10px] font-semibold leading-none text-[#86efcd] transition-colors" style={delay(d)}>{n}</span>
</a>

/** The hero's thesis: an answer is only as good as the sources you can open behind it. */
const GroundedAnswer = () => <div className="surface-shadow relative overflow-hidden rounded-[18px] border border-slate-200 bg-[#091426] text-white ring-1 ring-white/10 dark:border-white/10">
  <div className="flex items-center justify-between gap-3 border-b border-white/10 px-5 py-3.5">
    <div className="flex min-w-0 items-center gap-2.5"><span className="h-2 w-2 shrink-0 rounded-full bg-[#86efcd] shadow-[0_0_0_4px_rgba(134,239,205,.1)]" /><span className="truncate text-xs font-medium text-slate-300">Customer documentation</span></div>
    <span className="shrink-0 font-mono text-[10px] text-slate-500">48,212 docs in scope</span>
  </div>

  <div className="space-y-5 p-5 sm:p-6">
    <div className="cite-in flex justify-end" style={delay('.25s')}>
      <p className="max-w-[85%] rounded-[14px] rounded-br-[4px] bg-[#0b5cff] px-4 py-2.5 text-sm leading-6">How long do we keep files after someone deletes them?</p>
    </div>
    <div className="cite-in flex gap-3" style={delay('.6s')}>
      <span className="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-white/10"><Bot className="h-4 w-4 text-[#86efcd]" /></span>
      <p className="text-[15px] leading-7 text-slate-200">Deleted files stay recoverable for <strong className="font-semibold text-white">7 days</strong>, then they are permanently removed from storage and the search index<Cite n={1} d="1.1s" />. Organization exports follow the same window: the download link expires after 7 days<Cite n={2} d="1.35s" />.</p>
    </div>
  </div>

  <div className="border-t border-white/10 bg-white/[.025] px-5 py-4 sm:px-6">
    <p className="mb-3 text-[10px] font-bold uppercase tracking-[.18em] text-slate-500">Sources</p>
    <ol className="space-y-2">{sources.map((s, i) => <li key={s.n} id={`source-${s.n}`} className="cite-in grid grid-cols-[auto_1fr_auto] items-center gap-3 rounded-[10px] border border-white/10 bg-white/[.035] px-3 py-2.5" style={delay(`${1.1 + i * .25}s`)}>
      <span className="grid h-[18px] min-w-[18px] place-items-center rounded-[5px] bg-[#86efcd]/10 font-mono text-[10px] font-semibold text-[#86efcd]">{s.n}</span>
      <div className="min-w-0"><p className="truncate text-xs font-semibold text-slate-100">{s.title} <span className="font-normal text-slate-400">· {s.section}</span></p><p className="mt-0.5 truncate font-mono text-[10px] text-slate-500">{s.path}</p></div>
      <span className="font-mono text-[10px] text-slate-400" title="Retrieval relevance">{s.score}</span>
    </li>)}</ol>
  </div>

  <div className="flex items-center justify-between gap-4 border-t border-white/10 px-5 py-3 font-mono text-[10px] text-slate-500 sm:px-6">
    <span className="inline-flex items-center gap-1.5"><Check className="h-3 w-3 text-[#86efcd]" />2 of 2 claims cited</span>
    <span>answered in 812 ms</span>
  </div>
</div>

export default function Intro() {
  return <>
    <section className="relative overflow-hidden bg-white dark:bg-[#07101f]">
      <div className="enterprise-grid pointer-events-none absolute inset-0" />
      <div className="pointer-events-none absolute left-[48%] top-[-28rem] h-[48rem] w-[48rem] rounded-full bg-blue-500/10 blur-3xl dark:bg-blue-500/15" />
      <div className="relative mx-auto grid max-w-7xl items-center gap-14 px-5 pb-20 pt-20 lg:grid-cols-[1.05fr_.95fr] lg:px-8 lg:pb-28 lg:pt-24">
        <div className="animate-rise">
          <div className="mb-7 inline-flex items-center gap-2 rounded-full border border-blue-200 bg-blue-50 px-3 py-1.5 text-xs font-semibold text-[#0b5cff] dark:border-blue-400/20 dark:bg-blue-400/10 dark:text-blue-300"><Sparkles className="h-3.5 w-3.5" /> Hosted document intelligence</div>
          <h1 className="max-w-3xl text-[2.75rem] font-semibold leading-[1.02] tracking-[-.055em] text-balance text-slate-950 sm:text-6xl xl:text-[4.4rem] dark:text-white">Your documents. <span className="text-[#0b5cff] dark:text-[#5b8cff]">Answers you can verify.</span></h1>
          <p className="mt-7 max-w-xl text-lg leading-8 text-slate-600 dark:text-slate-300">Import files, folders, and websites into managed knowledge bases. Publish citation-backed AI assistants, instant search, and actionable content analytics from one secure catalogue.</p>
          <div className="mt-9 flex flex-col gap-3 sm:flex-row">
            <Link href="/signup" className="inline-flex items-center justify-center gap-2 rounded-[11px] bg-[#0b5cff] px-5 py-3.5 text-sm font-semibold text-white shadow-[0_12px_30px_rgba(11,92,255,.25)] transition hover:-translate-y-px hover:bg-[#084dcc]">Start free <ArrowRight className="h-4 w-4" /></Link>
            <Link href="#platform" className="inline-flex items-center justify-center gap-2 rounded-[11px] border border-slate-300 bg-white px-5 py-3.5 text-sm font-semibold text-slate-800 transition hover:border-slate-400 hover:bg-slate-50 dark:border-white/15 dark:bg-white/5 dark:text-white dark:hover:bg-white/10">See how it works <ChevronRight className="h-4 w-4" /></Link>
          </div>
          <div className="mt-8 flex flex-wrap gap-x-6 gap-y-3 text-xs font-medium text-slate-500 dark:text-slate-400">{['14-day Pro trial','Secure Stripe billing','Cancel anytime'].map(x => <span key={x} className="inline-flex items-center gap-2"><Check className="h-3.5 w-3.5 text-emerald-500" />{x}</span>)}</div>
        </div>

        <div className="animate-rise-delay relative lg:pl-5">
          <div className="absolute -inset-6 rounded-[2rem] bg-gradient-to-br from-blue-500/15 via-transparent to-emerald-300/20 blur-2xl" />
          <GroundedAnswer />
        </div>
      </div>
      <div className="relative mx-auto max-w-7xl border-t border-slate-200 px-5 py-8 lg:px-8 dark:border-white/10"><p className="text-center text-[10px] font-bold uppercase tracking-[.22em] text-slate-400">Built for high-trust teams at every stage</p><div className="mt-6 flex flex-wrap justify-center gap-x-10 gap-y-4 text-sm font-bold tracking-[.08em] text-slate-400 sm:justify-between dark:text-slate-500"><span>APERTURE</span><span>KINETIC</span><span>CATALYST</span><span>STRATUM</span><span>MONUMENT</span></div></div>
    </section>

    <section id="platform" className="bg-[#f6f8fb] py-24 dark:bg-[#0a1424] lg:py-32">
      <div className="mx-auto max-w-7xl px-5 lg:px-8">
        <div className="grid gap-10 lg:grid-cols-[.72fr_1.28fr] lg:gap-20">
          <div><p className="text-xs font-bold uppercase tracking-[.2em] text-[#0b5cff] dark:text-[#86efcd]">One content pipeline</p><h2 className="mt-5 text-4xl font-semibold leading-tight tracking-[-.045em] text-slate-950 lg:text-5xl dark:text-white">Store once. Search, ask, and learn everywhere.</h2></div>
          <p className="self-end text-lg leading-8 text-slate-600 dark:text-slate-300">{product.name} keeps your organization’s source files, metadata, semantic index, full-text search, assistants, and analytics in one governed account.</p>
        </div>
        <div className="mt-16 grid gap-8 sm:grid-cols-2 lg:grid-cols-4">
          <Feature icon={<Files className="h-5 w-5" />} title="Managed knowledge bases">Upload documents, synchronize folders, and crawl websites into durable catalogues.</Feature>
          <Feature icon={<Bot className="h-5 w-5" />} title="Grounded AI assistants">Answer from an approved document scope with citations customers can inspect.</Feature>
          <Feature icon={<Search className="h-5 w-5" />} title="Instant document search">Serve heading-aware full-text results without a model call or per-query AI cost.</Feature>
          <Feature icon={<BarChart3 className="h-5 w-5" />} title="First-party analytics">Find content gaps through search demand, no-result queries, clicks, and assistant usage.</Feature>
        </div>
      </div>
    </section>

    <section id="solutions" className="bg-white py-24 dark:bg-[#07101f] lg:py-32">
      <div className="mx-auto grid max-w-7xl gap-14 px-5 lg:grid-cols-2 lg:items-center lg:px-8">
        <div className="rounded-[18px] border border-slate-200 bg-[#f7f9fc] p-5 shadow-[0_24px_60px_rgba(16,24,40,.08)] dark:border-white/10 dark:bg-white/[.035] sm:p-7">
          <div className="flex items-center justify-between border-b border-slate-200 pb-5 dark:border-white/10"><div><p className="text-xs text-slate-500">Knowledge pipeline</p><p className="mt-1 font-semibold text-slate-950 dark:text-white">Customer documentation</p></div><span className="rounded-full bg-emerald-100 px-3 py-1 text-[11px] font-semibold text-emerald-700 dark:bg-emerald-400/10 dark:text-[#86efcd]">Fully synchronized</span></div>
          <div className="mt-5 space-y-3">{[['Source catalogue','Current','48,212 docs'],['Semantic index','Healthy','100% coverage'],['Search index','Ready','42 ms']].map(([name,status,detail]) => <div key={name} className="grid grid-cols-[1fr_auto_auto] items-center gap-5 rounded-xl border border-slate-200 bg-white px-4 py-4 text-sm dark:border-white/10 dark:bg-[#0c1729]"><div className="flex items-center gap-3"><span className="h-2 w-2 rounded-full bg-emerald-400"/><span className="font-medium text-slate-800 dark:text-slate-100">{name}</span></div><span className="text-slate-500">{status}</span><span className="min-w-20 text-right font-mono text-xs text-slate-400">{detail}</span></div>)}</div>
          <div className="mt-5 flex items-center gap-3 rounded-xl bg-[#0b5cff] p-4 text-white"><FileSearch className="h-5 w-5"/><div><p className="text-xs text-blue-100">Questions answered with citations</p><p className="mt-1 text-lg font-semibold">12,841 this month</p></div><BarChart3 className="ml-auto h-8 w-8 text-blue-200"/></div>
        </div>
        <div className="lg:pl-12"><p className="text-xs font-bold uppercase tracking-[.2em] text-[#0b5cff] dark:text-[#86efcd]">Grounded by design</p><h2 className="mt-5 text-4xl font-semibold leading-tight tracking-[-.045em] text-slate-950 lg:text-5xl dark:text-white">One governed source for every answer.</h2><p className="mt-6 text-lg leading-8 text-slate-600 dark:text-slate-300">Preview every import, keep unchanged documents out of re-indexing, enforce server-side scopes, and open citations against the canonical source.</p><div className="mt-8 space-y-4">{[[LockKeyhole,'Server-enforced document scopes'],[Database,'Durable, restart-safe indexing'],[Globe2,'Canonical citations across every site']].map(([Icon,label]) => { const I = Icon as typeof LockKeyhole; return <div key={label as string} className="flex items-center gap-3 text-sm font-semibold text-slate-800 dark:text-slate-100"><span className="grid h-8 w-8 place-items-center rounded-lg bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10 dark:text-blue-300"><I className="h-4 w-4" /></span>{label as string}</div>})}</div></div>
      </div>
    </section>

    <section className="bg-[#0a1424] py-20 text-white">
      <div className="mx-auto flex max-w-7xl flex-col items-start justify-between gap-8 px-5 lg:flex-row lg:items-center lg:px-8"><div><p className="text-xs font-bold uppercase tracking-[.2em] text-[#86efcd]">Make your knowledge useful</p><h2 className="mt-4 text-3xl font-semibold tracking-[-.04em] sm:text-4xl">Turn your document estate into answers, search, and insight.</h2></div><Link href="/signup" className="inline-flex shrink-0 items-center gap-2 rounded-[11px] bg-white px-5 py-3.5 text-sm font-semibold text-[#0a1424] transition hover:-translate-y-px hover:bg-blue-50">Create your knowledge base <ArrowRight className="h-4 w-4" /></Link></div>
    </section>
  </>
}
