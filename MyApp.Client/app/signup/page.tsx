'use client'

import { SyntheticEvent, Suspense, useEffect, useState } from "react"
import { useClient, FormLoading, ErrorSummary, TextInput, PrimaryButton, SecondaryButton, ApiStateContext } from "@servicestack/react"
import { serializeToObject, leftPart, rightPart, toPascalCase } from "@servicestack/client"
import {useRouter, useSearchParams} from "next/navigation"
import Page from "@/components/layout-page"
import { getRedirect } from "@/lib/gateway"
import { Register, RegisterResponse } from "@/lib/dtos"
import { appAuth, Redirecting } from "@/lib/auth"

function SignUpContent() {

    const client = useClient()
    const [displayName, setDisplayName] = useState<string>()
    const [username, setUsername] = useState<string>()
    const [password, setPassword] = useState<string>()
    const [confirmPassword, setConfirmPassword] = useState<string>()
    const router = useRouter()
    const searchParams = useSearchParams()
    const [accountKind, setAccountKind] = useState<'Individual' | 'Business'>(searchParams.get('account') === 'business' ? 'Business' : 'Individual')
    const [organizationName, setOrganizationName] = useState('')
    const { user, revalidate } = appAuth()

    const setUser = (email: string) => {
        let first = leftPart(email, '@');
        let last = rightPart(leftPart(email, '.'), '@')
        setDisplayName(toPascalCase(first) + ' ' + toPascalCase(last))
        setUsername(email)
        setPassword('p@55wOrd')
        setConfirmPassword('p@55wOrd')
    }

    useEffect(() => {
        if (user) {
            const redirect = getRedirect(Object.fromEntries(searchParams.entries())) || "/dashboard"
            router.replace(redirect)
        }
    }, [user])
    if (user) return <Redirecting/>

    const onSubmit = async (e: SyntheticEvent<HTMLFormElement>) => {
        e.preventDefault()

        const {displayName, userName, password, confirmPassword, autoLogin} = serializeToObject(e.currentTarget);
        if (password !== confirmPassword) {
            client.setError({fieldName: 'confirmPassword', message: 'Passwords do not match'})
            return
        }

        const returnUrl = getRedirect(searchParams)
        if (accountKind === 'Business' && (organizationName.trim().length < 2 || organizationName.trim().length > 100)) {
            client.setError({fieldName: 'organizationName', message: 'Enter an organization name between 2 and 100 characters.'})
            return
        }
        const api = await client.api(new Register({
            displayName,
            email: userName,
            password,
            confirmPassword,
            autoLogin,
            meta: { ...(returnUrl ? { returnUrl } : {}), accountKind, ...(accountKind === 'Business' ? { organizationName: organizationName.trim() } : {}) },
        }))
        if (api.succeeded) {
            await revalidate()
            const redirectUrl = (api.response as RegisterResponse).redirectUrl
            if (redirectUrl) {
                location.href = redirectUrl
            } else {
                router.push("/signin")
            }
        }
    }

    return (
        <>
            <ApiStateContext.Provider value={client}>
                <section className="overflow-hidden rounded-[16px] border border-slate-200 bg-white shadow-[0_24px_60px_rgba(16,24,40,.10)] dark:border-white/10 dark:bg-[#0c1729]">
                    <form onSubmit={onSubmit} className="max-w-prose">
                        <div>
                            <ErrorSummary except="organizationName,displayName,userName,password,confirmPassword"/>
                            <div className="space-y-6 bg-white px-6 py-7 dark:bg-[#0c1729]">
                                <div role="group" aria-label="Account type" className="grid grid-cols-2 gap-1 rounded-xl border border-slate-200 bg-slate-50 p-1 dark:border-white/10 dark:bg-white/[.035]">
                                    {(['Individual', 'Business'] as const).map(kind => (
                                        <button key={kind} type="button" aria-pressed={accountKind === kind} onClick={() => setAccountKind(kind)}
                                            className={`rounded-lg px-4 py-2.5 text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#0b5cff] ${accountKind === kind ? 'bg-white text-slate-950 shadow-sm dark:bg-white/10 dark:text-white' : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-white'}`}>
                                            {kind === 'Individual' ? 'Personal' : 'Business'}
                                        </button>
                                    ))}
                                </div>
                                <div className="flex flex-col gap-y-4">
                                    {accountKind === 'Business' && <TextInput id="organizationName" label="Organization name" required minLength={2} maxLength={100}
                                        value={organizationName} onChange={setOrganizationName}/>}
                                    <TextInput id="displayName" help="Your first and last name" autoComplete="name"
                                               value={displayName} onChange={setDisplayName}/>
                                    <TextInput id="userName" autoComplete="email"
                                               value={username} onChange={setUsername}/>
                                    <TextInput id="password" type="password" help="6 characters or more"
                                               autoComplete="new-password"
                                               value={password} onChange={setPassword}/>
                                    <TextInput id="confirmPassword" type="password" value={confirmPassword} onChange={setConfirmPassword}/>
                                </div>
                            </div>
                            <div className="border-t border-slate-200 bg-slate-50 px-6 py-4 text-right dark:border-white/10 dark:bg-white/[.025]">
                                <div className="flex justify-end">
                                    { client.loading ? <FormLoading className="flex-1"/> : null }
                                    <PrimaryButton className="ml-3 !bg-[#0b5cff] !px-6 !py-3">{accountKind === 'Business' ? 'Create organization' : 'Create account'}</PrimaryButton>
                                </div>
                            </div>
                        </div>
                    </form>
                </section>
            </ApiStateContext.Provider>

            <div className="flex mt-8 ml-8">
                <h3 className="mr-4 leading-8 text-gray-500 dark:text-gray-400">Quick Links</h3>
                <div className="flex flex-wrap max-w-lg gap-2">
                    <SecondaryButton onClick={() => setUser('new@user.com')}>
                        new@user.com
                    </SecondaryButton>
                </div>
            </div>
        </>
    )
}

export default function SignUp() {
    return (
        <Page title="Sign Up">
            <Suspense fallback={<div>Loading...</div>}>
                <SignUpContent />
            </Suspense>
        </Page>
    )
}
