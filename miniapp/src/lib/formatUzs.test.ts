import { describe, expect, it } from 'vitest'
import { formatUzs } from './formatUzs'

describe('formatUzs', () => {
  it('groups thousands', () => {
    expect(formatUzs(1250000)).toBe('1 250 000')
    expect(formatUzs(550000)).toBe('550 000')
    expect(formatUzs(999)).toBe('999')
    expect(formatUzs(0)).toBe('0')
  })
  it('handles negatives and bigint', () => {
    expect(formatUzs(-45000)).toBe('-45 000')
    expect(formatUzs(45200000n)).toBe('45 200 000')
  })
})
