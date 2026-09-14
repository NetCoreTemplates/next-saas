'use client'

import {serializeToObject} from "@servicestack/client"
import {SyntheticEvent, Suspense, useEffect, useState} from "react"
import {useRouter, useSearchParams} from "next/navigation"
import Link from "next/link"

import Page from "@/components/layout-page"
import {ErrorSummary, TextInput, PrimaryButton, SecondaryButton, useClient, ApiStateContext} from "@servicestack/react"
import {Authenticate} from "@/lib/dtos"
import {appAuth, Redirecting} from "@/lib/auth"
import {getRedirect} from "@/lib/gateway"

function SignInContent() {

    const client = useClient()
    const [userName, setUserName] = useState<string|undefined>()
    const [password, setPassword] = useState<string|undefined>()

    const setUser = (email: string) => {
        setUserName(email)
        setPassword('p@55wOrd')
    }
    const router = useRouter()
    const searchParams = useSearchParams()
    const returnUrl = getRedirect(searchParams)

    const {user, revalidate} = appAuth()
    useEffect(() => {
        if (user) {
            const redirect = getRedirect(Object.fromEntries(searchParams.entries())) || "/dashboard"
            router.replace(redirect)
        }
    }, [user]);
    if (user) return <Redirecting/>

    const onSubmit = async (e: SyntheticEvent<HTMLFormElement>) => {
        e.preventDefault()
        const api = await client.api(new Authenticate({ provider:'credentials', userName, password }))
        if (api.succeeded)
            await revalidate()
    }

    return (
        <>
            <ApiStateContext.Provider value={client}>
                <section className="overflow-hidden rounded-[16px] border border-slate-200 bg-white shadow-[0_24px_60px_rgba(16,24,40,.10)] dark:border-white/10 dark:bg-[#0c1729]">
                    <form onSubmit={onSubmit}>
                        <div>
                            <ErrorSummary except="userName,password"/>
                            <div className="space-y-6 bg-white px-6 py-7 dark:bg-[#0c1729]">
                                <div className="flex flex-col gap-y-4">
                                    <TextInput id="userName" help="Email you signed up with" autoComplete="email"
                                               value={userName} onChange={setUserName}/>
                                    <TextInput id="password" type="password" help="6 characters or more"
                                               autoComplete="current-password"
                                               value={password} onChange={setPassword}/>
                                </div>

                                <div>
                                    <PrimaryButton className="w-full !bg-[#0b5cff] !py-3">Log in</PrimaryButton>
                                </div>

                                <div className="mt-8 text-sm">
                                    <p className="mb-3">
                                        <Link className="font-semibold" href={returnUrl ? `/signup?redirect=${encodeURIComponent(returnUrl)}` : '/signup'}>Register as a new user</Link>
                                    </p>
                                </div>
                            </div>

                        </div>
                    </form>
                </section>
            </ApiStateContext.Provider>
            <div className="mt-7 rounded-xl border border-slate-200 bg-white/70 p-4 dark:border-white/10 dark:bg-white/[.035]">
                <h3 className="mb-3 text-xs font-semibold text-slate-500">Development accounts</h3>
                <div className="flex flex-wrap max-w-lg gap-2">
                    <SecondaryButton onClick={() => setUser('admin@email.com')}>
                        admin@email.com
                    </SecondaryButton>
                    <SecondaryButton onClick={() => setUser('manager@email.com')}>
                        manager@email.com
                    </SecondaryButton>
                    <SecondaryButton onClick={() => setUser('employee@email.com')}>
                        employee@email.com
                    </SecondaryButton>
                    <SecondaryButton onClick={() => setUser('new@user.com')}>
                        new@user.com
                    </SecondaryButton>
                </div>
            </div>
        </>
    )
}

export default function SignIn() {
    return (
        <Page title="Use a local account to log in.">
            <Suspense fallback={<div>Loading...</div>}>
                <SignInContent />
            </Suspense>
        </Page>
    )
}
