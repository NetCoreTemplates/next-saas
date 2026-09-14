import Intro from "@/components/intro"
import Layout from "@/components/layout"
import type { Metadata } from 'next'

export const metadata: Metadata = {
  title: 'Document intelligence, grounded',
}

export default function Index() {

  return (
    <Layout>
      <Intro />
    </Layout>
  )
}
