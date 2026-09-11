import Intro from "@/components/intro"
import Layout from "@/components/layout"
import { CMS_NAME } from "@/lib/constants"
import type { Metadata } from 'next'

export const metadata: Metadata = {
  title: `Next.js Example with ${CMS_NAME}`,
}

export default function Index() {

  return (
    <Layout>
      <div className="max-w-7xl mx-auto px-5">
          <Intro />
      </div>
    </Layout>
  )
}
