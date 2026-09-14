'use client'
import AdminCenterPage from '@/components/admin-center-page'
import { ValidateAuth } from '@/lib/auth'
function Page(){return <AdminCenterPage section="operations"/>}
export default ValidateAuth(Page,{roles:['Admin','BillingAdmin']})
