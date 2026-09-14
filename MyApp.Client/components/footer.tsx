
import { product, productInitial } from '@/lib/product'

const groups = [
  { title: 'Product', links: [{ label: 'Knowledge bases', href: '/knowledge-bases' }, { label: 'Assistants', href: '/assistants' }] },
  { title: 'Explore', links: [{ label: 'Search', href: '/search' }, { label: 'Analytics', href: '/analytics' }] },
  { title: 'Company', links: [{ label: 'About', href: '/about' }, { label: 'Contact', href: '/contact' }] },
  { title: 'Legal', links: [{ label: 'Privacy', href: product.privacyUrl }, { label: 'Terms', href: product.termsUrl }] },
]

const Footer = () => {
  return (
    <footer className="border-t border-slate-200 bg-white dark:border-white/10 dark:bg-[#07101f]">
      <div className="mx-auto max-w-7xl px-5 py-14 lg:px-8">
        <div className="grid gap-10 border-b border-slate-200 pb-12 md:grid-cols-[1.4fr_2fr] dark:border-white/10">
          <div>
            <div className="flex items-center gap-3"><span className="grid h-9 w-9 place-items-center rounded-[10px] bg-[#0b5cff] text-sm font-black text-white">{productInitial}</span><span className="font-bold tracking-[-.03em]">{product.name}</span></div>
            <p className="mt-4 max-w-sm text-sm leading-6 text-slate-500 dark:text-slate-400">{product.description}</p>
          </div>
          <nav aria-label="Footer" className="grid grid-cols-2 gap-8 text-sm sm:grid-cols-4">
            {groups.map(group => <div key={group.title}><p className="font-semibold text-slate-950 dark:text-white">{group.title}</p>{group.links.map(link => <a key={link.href} href={link.href} className="mt-3 block text-slate-500 transition hover:text-[#0b5cff] dark:text-slate-400">{link.label}</a>)}</div>)}
          </nav>
        </div>
        <div className="flex flex-col gap-3 pt-6 text-xs text-slate-400 sm:flex-row sm:items-center sm:justify-between"><p>© {new Date().getFullYear()} {product.organizationName}</p><p>Built on ServiceStack · Next.js · Stripe</p></div>
      </div>
    </footer>
  )
}

export default Footer
