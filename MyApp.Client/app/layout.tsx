import "../styles/index.css"
import type { Metadata } from 'next'
import Providers from './providers'
import { product } from '@/lib/product'

export const metadata: Metadata = {
  title: {
    default: `${product.name} — Document intelligence, grounded`,
    template: `%s · ${product.name}`,
  },
  description: product.description,
}

export default function RootLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <html lang="en" className="h-full" suppressHydrationWarning>
      <head />
      <body className="bg-[#f6f8fb] text-[#101828] antialiased transition-colors duration-200 dark:bg-[#07101f] dark:text-white">
        <script
          dangerouslySetInnerHTML={{
            __html: `
              (function() {
                function getTheme() {
                  const theme = localStorage.getItem('color-scheme');
                  if (theme) return theme;
                  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
                }
                const theme = getTheme();
                if (theme === 'dark') {
                  document.documentElement.classList.add('dark');
                }
              })();
            `,
          }}
        />
        <Providers>{children}</Providers>
      </body>
    </html>
  )
}
