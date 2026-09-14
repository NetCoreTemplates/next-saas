import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import FeatureGate, { hasFeature } from './feature-gate'

vi.mock('next/link', () => ({ default: ({children,href}:{children:React.ReactNode,href:string}) => <a href={href}>{children}</a> }))

describe('FeatureGate',()=>{
  it('renders entitled content',()=>{
    const entitlements=[{key:'audit.read',displayName:'Audit',enabled:true,source:'Plan'}]
    expect(hasFeature(entitlements,'audit.read')).toBe(true)
    render(<FeatureGate feature="audit.read" entitlements={entitlements}><p>Audit events</p></FeatureGate>)
    expect(screen.getByText('Audit events')).toBeTruthy()
  })

  it('renders an upgrade explanation when disabled',()=>{
    render(<FeatureGate feature="audit.read" entitlements={[{key:'audit.read',displayName:'Audit',enabled:false,source:'Plan'}]}><p>Private</p></FeatureGate>)
    expect(screen.queryByText('Private')).toBeNull()
    expect(screen.getByRole('link',{name:'Compare plans'}).getAttribute('href')).toBe('/billing')
  })
})
