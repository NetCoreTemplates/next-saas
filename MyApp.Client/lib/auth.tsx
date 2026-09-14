'use client'

import React, { useContext, useEffect } from "react"
import { useRouter, usePathname } from "next/navigation"
import { useAuth, Loading } from "@servicestack/react"
import { client, Routes } from "./gateway"
import { Authenticate } from "@/lib/dtos"
import { AuthReadyContext } from "@/app/providers"

export const Redirecting = () => {
  return <Loading className="py-2 pl-4">redirecting ...</Loading>
}

type ValidateAuthProps = {
  role?: string
  roles?: string[]
  permission?: string
  redirectTo?: string
}
export function ValidateAuth<TOriginalProps extends {}>(Component:React.FC<TOriginalProps>, validateProps? :ValidateAuthProps) {
    let { role, roles, permission, redirectTo } = validateProps ?? {}
    const compWithProps: React.FC<TOriginalProps> = (props) => {
        const router = useRouter()
        const pathname = usePathname()
        const authReady = useContext(AuthReadyContext)
        const authProps = useAuth()
        const { user, isAuthenticated, hasRole, hasPermission } = authProps
        const target = redirectTo ?? pathname
        const shouldRedirect = () => !isAuthenticated
            ? Routes.signin(target)
            : role && !hasRole(role)
                ? Routes.forbidden()
                : roles?.length && !roles.some(x => hasRole(x))
                    ? Routes.forbidden()
                : permission && !hasPermission(permission)
                    ? Routes.forbidden()
                    : null

        useEffect(() => {
            if (!authReady) return
            const goTo = shouldRedirect()
            if (goTo) {
                router.replace(goTo)
            }
        }, [authReady, user, pathname, router])

        if (!authReady) {
            return <Redirecting />
        }

        if (shouldRedirect()) {
            return <Redirecting />
        }

        return <Component {...props} />
    }

    return compWithProps
}

export function appAuth() {
    const router = useRouter()
    const authState = useAuth()
    async function revalidate() {
        try {
            const response = await client.post(new Authenticate())
            authState.signIn(response)
        } catch {
            authState.signOut()
        }
    }
    async function signOut(redirectTo?:string) {
        await client.post(new Authenticate({ provider: 'logout' }))
        authState.signOut()
        if (redirectTo) {
            router.push(redirectTo)
        }
    }
    return { ...authState, revalidate, signOut }
}
