import { describe, expect, it } from 'vitest'
import { formatCompact, formatUzs, parseAmountInput } from './formatUzs'

const plain = (s: string) => s.replace(/\u00a0/g, ' ')

describe('formatUzs', () => {
  it('groups thousands', () => {
    expect(plain(formatUzs(1250000))).toBe('1 250 000')
    expect(plain(formatUzs(550000))).toBe('550 000')
    expect(formatUzs(999)).toBe('999')
    expect(formatUzs(0)).toBe('0')
  })

  it('handles negatives and bigint', () => {
    expect(plain(formatUzs(-50000))).toBe('-50 000')
    expect(plain(formatUzs(1000000000000n))).toBe('1 000 000 000 000')
  })

  it('uses a no-break space so amounts never wrap', () => {
    expect(formatUzs(1000)).toBe('1\u00a0000')
  })
})

describe('formatCompact', () => {
  it.each([
    [45_200_000, '45.2M'],
    [1_000_000, '1M'],
    [550_000, '550K'],
    [1_500, '1.5K'],
    [900, '900'],
    [0, '0'],
    [-2_300_000, '-2.3M'],
    [3_400_000_000, '3.4B'],
  ])('%d → %s', (value, expected) => expect(formatCompact(value)).toBe(expected))
})

describe('parseAmountInput', () => {
  it('keeps digits only', () => {
    expect(parseAmountInput('550 000')).toBe(550000)
    expect(parseAmountInput('1\u00a0250\u00a0000')).toBe(1250000)
    expect(parseAmountInput("550.000 so'm")).toBe(550000)
    expect(parseAmountInput('')).toBe(0)
    expect(parseAmountInput('abc')).toBe(0)
  })

  it('caps at the backend limit', () => {
    expect(parseAmountInput('99999999999999999')).toBe(1_000_000_000_000)
  })
})
