'use client'
import AdminCenterPage from '@/components/admin-center-page'
import { ValidateAuth } from '@/lib/auth'
function Page(){return <AdminCenterPage section="usage"/>}
export default ValidateAuth(Page,{role:'Admin'})
