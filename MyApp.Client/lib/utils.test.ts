import { describe, it, expect } from 'vite-plus/test'
import { formatCurrency, dateInputFormat, enforcementLabel, meterKindLabel, formatMeterUnits, formText } from './utils'

describe('utils', () => {
  describe('formatCurrency', () => {
    it('should format number as USD currency', () => {
      expect(formatCurrency(100)).toBe('$100.00')
      expect(formatCurrency(1234.56)).toBe('$1,234.56')
    })

    it('should return empty string for falsy values', () => {
      expect(formatCurrency(undefined)).toBe('')
      expect(formatCurrency(0)).toBe('')
    })
  })

  describe('dateInputFormat', () => {
    it('should format date with dashes', () => {
      const date = new Date('2025-11-11T00:00:00Z')
      const result = dateInputFormat(date)
      // Result will be in format like "11-11-2025" or "2025-11-11" depending on locale
      expect(result).toMatch(/\d{1,4}-\d{1,2}-\d{1,4}/)
    })
  })

  describe('meter labels', () => {
    it('should humanize enforcement and meter kinds', () => {
      expect(enforcementLabel('HardLimit')).toBe('Hard limit')
      expect(enforcementLabel('SoftLimit')).toBe('Soft limit')
      expect(meterKindLabel('Gauge')).toBe('Current total')
      expect(meterKindLabel('Counter')).toBe('Per period')
    })

    it('should pass through unknown values and blank missing ones', () => {
      expect(enforcementLabel('Custom')).toBe('Custom')
      expect(enforcementLabel(undefined)).toBe('')
    })
  })

  describe('formatMeterUnits', () => {
    it('should format storage meters as bytes', () => {
      expect(formatMeterUnits('storage.bytes', 512)).toBe('512 B')
      expect(formatMeterUnits('storage.bytes', 2048)).toBe('2.0 KB')
      expect(formatMeterUnits('storage.bytes', 500 * 1024 ** 2)).toBe('500.0 MB')
    })

    it('should format other meters as counts', () => {
      expect(formatMeterUnits('api.requests', 1500)).toBe((1500).toLocaleString())
    })
  })

  describe('formText', () => {
    it('should read text fields and default missing or file fields to empty', () => {
      const form = new FormData()
      form.set('name', 'Acme')
      form.set('upload', new Blob(['x']), 'x.txt')
      expect(formText(form, 'name')).toBe('Acme')
      expect(formText(form, 'missing')).toBe('')
      expect(formText(form, 'upload')).toBe('')
    })
  })
})
