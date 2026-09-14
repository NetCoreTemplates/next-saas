'use client'

import { createContext, useEffect, useState } from "react"
import Link from 'next/link'
import { setLinkComponent, ClientContext } from '@servicestack/react'
import { client, init } from "@/lib/gateway"

// Adapter component to convert react-router 'to' prop to Next.js 'href' prop
const NextLink = ({ to, ...props }: any) => <Link href={to || props.href} {...props} />

setLinkComponent(NextLink)

export const AuthReadyContext = createContext(false)

let initialization: Promise<unknown> | undefined
const initialize = () => initialization ??= Promise.resolve(init())

export default function Providers({ children }: { children: React.ReactNode }) {
  const [authReady, setAuthReady] = useState(false)

  useEffect(() => {
    let active = true
    void initialize().finally(() => {
      if (active) setAuthReady(true)
    })
    return () => { active = false }
  }, [])

  return (
    <ClientContext.Provider value={client}>
      <AuthReadyContext.Provider value={authReady}>
        {children}
      </AuthReadyContext.Provider>
    </ClientContext.Provider>
  )
}
