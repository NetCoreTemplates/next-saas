# Add a frontend page

Authenticated product pages are client-rendered React routes that call typed ServiceStack APIs and remain compatible with Next.js static export.

[Development recipes](README.md) · [Architecture](../concepts/architecture.md)

## 1. Create the route

Add `MyApp.Client/app/widgets/page.tsx`:

```tsx
'use client'

import { useCallback, useEffect, useState } from 'react'
import AppShell, { PageHeading, Panel } from '@/components/app-shell'
import { ValidateAuth } from '@/lib/auth'
import { client } from '@/lib/gateway'
import { QueryWidgets, WidgetInfo } from '@/lib/dtos'

function WidgetsPage() {
  const [items, setItems] = useState<WidgetInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()

  const load = useCallback(async () => {
    setLoading(true)
    const api = await client.api(new QueryWidgets())
    if (api.succeeded) {
      setItems(api.response?.results ?? [])
      setError(undefined)
    } else {
      setError(api.error?.message ?? 'Unable to load widgets.')
    }
    setLoading(false)
  }, [])

  useEffect(() => { void load() }, [load])

  return <AppShell>
    <PageHeading
      eyebrow="Organization product"
      title="Widgets"
      description="Create and manage this organization’s widgets."
    />
    <Panel className="p-6">
      {loading ? 'Loading…' : error ?? `${items.length} widgets`}
    </Panel>
  </AppShell>
}

export default ValidateAuth(WidgetsPage)
```

Use `Layout` for public marketing/content pages and `AppShell` plus `ValidateAuth` for product pages.

## 2. Add navigation and entitlement behavior

Add the route to the appropriate `AppShell` navigation group. Use `FeatureGate` for customer explanation and upgrade affordances, while keeping the matching `RequiresFeature` on the backend DTO.

Reuse `PageHeading`, `Panel`, `StatusPill`, Lucide icons, and tokens in `styles/index.css`. Preserve the enterprise visual system instead of introducing page-local colors and component conventions.

![Module Frontend User Interface](../assets/reference-module-ui.png)

## 3. Keep effects stable

Memoize loader functions with `useCallback`, list only stable scalar dependencies, and trigger deliberate refreshes after mutations. Do not place response objects, newly created request objects, or unstable callbacks in effect dependencies.

For request races, ignore stale responses or use the guarded pattern in `useUsageAnalytics`. This prevents recursive fetching, flicker, and an older request overwriting a newer selection.

## 4. Preserve static export

Do not add Route Handlers, Server Actions, request-time server components, cookies/headers APIs, or Node-only runtime behavior. Dynamic state comes from the ASP.NET-hosted ServiceStack API after hydration.

Public pages may use a static fallback when appropriate, but connected API data should replace it.

## 5. Verify

Test loading, empty, success, validation, forbidden, feature-disabled, read-only, API failure, dark mode, and responsive layouts. Run `npm run typecheck`, `npm run test:run`, and `npm run build`; confirm the route appears in the static route table.

## Related documentation

- [Generate typed DTOs](generate-typed-dtos.md)
- [Add a feature gate](add-a-feature-gate.md)
- [Testing](testing.md)

