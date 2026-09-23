import { dateFmt, toDate, toDateFmt } from "@servicestack/client"
import { type ClassValue, clsx } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

const formatter = new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
})
export const formatCurrency = (n?:number) => n ? formatter.format(n) : ''
export const formatDate = (s?:string) => s ? toDateFmt(s) : ''

export const dateInputFormat = (d:Date) => dateFmt(d).replace(/\//g,'-')

export function sanitizeForUi(dto:any) {
    if (!dto) return {}
    Object.keys(dto).forEach(key => {
        let value = dto[key]
        if (typeof value == 'string') {
            if (value.startsWith('/Date'))
                dto[key] = dateInputFormat(toDate(value))
        }
    })
    return dto
}

const enforcementLabels: Record<string,string> = { HardLimit:'Hard limit', SoftLimit:'Soft limit', MeteredOverage:'Metered overage' }
const meterKindLabels: Record<string,string> = { Counter:'Per period', Gauge:'Current total', ReservableCounter:'Per period' }

/** Customer-facing name for a quota enforcement mode. */
export const enforcementLabel = (value?:string) => value ? enforcementLabels[value] ?? value : ''
/** Customer-facing name for how a meter accumulates: counters reset each period, gauges measure what exists now. */
export const meterKindLabel = (value?:string) => value ? meterKindLabels[value] ?? value : ''

/** Formats a meter value in its natural unit; storage meters are byte counts. */
export function formatMeterUnits(key?:string, value?:number) {
    const n = value ?? 0
    if (key !== 'storage.bytes') return n.toLocaleString()
    if (n >= 1024 ** 3) return `${(n / 1024 ** 3).toFixed(1)} GB`
    if (n >= 1024 ** 2) return `${(n / 1024 ** 2).toFixed(1)} MB`
    if (n >= 1024) return `${(n / 1024).toFixed(1)} KB`
    return `${n.toLocaleString()} B`
}
