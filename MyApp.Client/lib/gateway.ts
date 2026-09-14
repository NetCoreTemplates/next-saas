import { JsonServiceClient, combinePaths } from "@servicestack/client"
import { Authenticate } from "@/lib/dtos"

export const Routes = {
    signin: (redirectTo?: string) => redirectTo ? `/signin?redirect=${redirectTo}` : `/signin`,
    forbidden: () => '/forbidden',
}

// Base URL configuration
const getBaseUrl = () => {
  if (typeof window === 'undefined') {
    // Server-side (during build): use absolute URL if available
    // This is needed for generateStaticParams to fetch data during build
    return process.env.INTERNAL_API_URL || process.env.apiBaseUrl || '';
  }
  // The production export is served by ASP.NET on the same origin. During
  // Next.js development, apiBaseUrl points at the local ASP.NET host.
  return process.env.apiBaseUrl || '/';
};

export const BaseUrl = getBaseUrl()
export const client = new JsonServiceClient(BaseUrl);

// Load Metadata & Auth State on Startup
// This needs to be called on client side only
export async function init() {
    if (typeof window === 'undefined') return

    const { useMetadata, authContext } = await import("@servicestack/react")
    const metadata = useMetadata(client)
    const authCtx = authContext()

    return await Promise.all([
        metadata.loadMetadata({
            olderThan: BaseUrl == '/' || location.search.includes('clear=metadata') 
                ? 0 
                : 60 * 60 * 1000 //1hr 
        }),
        client.post(new Authenticate())
            .then(r => {
                authCtx.signIn(r)
            }).catch(() => {
            authCtx.signOut()
        })
    ])
}

export function getRedirect(searchParams: URLSearchParams | Record<string, string | string[] | undefined>) {
    const redirect = searchParams instanceof URLSearchParams
        ? searchParams.get('redirect')
        : searchParams['redirect']
    return redirect && Array.isArray(redirect)
        ? redirect[0]
        : redirect
}
