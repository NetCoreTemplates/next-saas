import { FC } from "react"
import Head from "next/head"
import Nav from "./nav"
import Footer from "./footer"
import Meta from "./meta"

type Props = {
  title: string,
  description?: string,
  className?: string,
  bodyClass?: string,
  children: React.ReactNode,
}

const Page: FC<Props> = ({ title, description = "Continue to your organization.", className, bodyClass, children }) => {
  return (
    <>
      <Meta />
      <Head>
        <title>{title}</title>
      </Head>
      <Nav />
      <div className="relative min-h-[calc(100vh-4.5rem)] overflow-hidden bg-[#f6f8fb] dark:bg-[#07101f]">
        <div className="enterprise-grid pointer-events-none absolute inset-0" />
        <main className="relative py-14 lg:py-20">
          <div className={`mx-auto max-w-xl px-5 ${className ?? ''}`}>
            <div className={bodyClass ?? 'max-w-xl'}>
              <p className="text-xs font-bold uppercase tracking-[.2em] text-[#0b5cff] dark:text-[#86efcd]">Secure organization access</p>
              <h1 className="my-4 text-4xl font-semibold tracking-[-.045em] text-slate-950 dark:text-white">{title}</h1>
              <p className="mb-8 text-sm leading-6 text-slate-500 dark:text-slate-400">{description}</p>
              {children}
            </div>
          </div>
        </main>
      </div>
      <Footer />
    </>
  )
}

export default Page
