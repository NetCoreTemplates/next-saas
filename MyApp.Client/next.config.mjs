import createMDX from '@next/mdx'
import { readFileSync } from 'node:fs'

const appSettings = JSON.parse(readFileSync(new URL('../MyApp/appsettings.json', import.meta.url), 'utf8'))
const product = appSettings.Product ?? {}
const seedPlans = JSON.parse(readFileSync(new URL('../MyApp/plans.json', import.meta.url), 'utf8'))

const target = process.env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${process.env.ASPNETCORE_HTTPS_PORT}` :
    process.env.ASPNETCORE_URLS ? process.env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:5001';

const isProd = process.env.NODE_ENV === 'production'
const buildLocal = process.env.MODE === 'local'

// Define DEPLOY_API first
const DEPLOY_API = process.env.KAMAL_DEPLOY_HOST 
    ? `https://${process.env.KAMAL_DEPLOY_HOST}` 
    : target

// Now use it for API_URL
const API_URL = isProd ? DEPLOY_API : (buildLocal ? '' : target)

/**
 * @type {import('next').NextConfig}
 **/
const nextConfig = {
    // Use the compiler API so production builds are reliable on newer Node runtimes.
    experimental: {
        useTypeScriptCli: false
    },

    // Configure pageExtensions to include MDX files
    pageExtensions: ['js', 'jsx', 'md', 'mdx', 'ts', 'tsx'],

    // Enable static export (replaces next export command)
    output: isProd ? 'export' : undefined,

    // Change output directory from 'out' to 'dist'
    distDir: 'dist',

    // Images are served unoptimized; adjust if you later add an image optimizer/CDN
    images: {
        unoptimized: true
    },

    // Keep clean URLs without trailing slashes
    trailingSlash: false,

    env: {
        apiBaseUrl: API_URL,
        productName: product.ProductName ?? 'Acme',
        organizationName: product.OrganizationName ?? product.ProductName ?? 'Acme',
        productDescription: product.Description ?? '',
        supportEmail: product.SupportEmail ?? 'support@example.com',
        salesEmail: product.SalesEmail ?? 'sales@example.com',
        privacyUrl: product.PrivacyUrl ?? '/privacy',
        termsUrl: product.TermsUrl ?? '/terms',
        seedPlans: JSON.stringify(seedPlans),
    },
}

const withMDX = createMDX({
    extension: /\.(md|mdx)$/,
})

export default withMDX(nextConfig)
