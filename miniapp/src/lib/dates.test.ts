import { describe, expect, it } from 'vitest'
import { addDays, addMonths, daysBetween, endOfMonth, formatDate, formatDateTime, formatDayMonth, periodRange, todayIn } from './dates'

describe('dates', () => {
  it('adds days and months across month and year ends', () => {
    expect(addDays('2026-10-09', 7)).toBe('2026-10-16')
    expect(addDays('2026-10-30', 3)).toBe('2026-11-02')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addMonths('2026-10-09', 1)).toBe('2026-11-09')
    expect(addMonths('2027-01-31', 1)).toBe('2027-02-28')
  })

  it('end of month moves to the next month on the last day (same rule as the bot)', () => {
    expect(endOfMonth('2026-10-09')).toBe('2026-10-31')
    expect(endOfMonth('2026-10-31')).toBe('2026-11-30')
    expect(endOfMonth('2028-02-10')).toBe('2028-02-29')
  })

  it('counts days between dates with sign', () => {
    expect(daysBetween('2026-10-09', '2026-10-12')).toBe(3)
    expect(daysBetween('2026-10-09', '2026-10-06')).toBe(-3)
    expect(daysBetween('2026-10-09', '2026-10-09')).toBe(0)
  })

  it('formats calendar dates', () => {
    expect(formatDate('2026-10-16')).toBe('16.10.2026')
    expect(formatDayMonth('2026-03-05')).toBe('05.03')
  })

  it('shows instants in the shop time zone, not the device one', () => {
    // 20:30 UTC on the 9th is already 01:30 on the 10th in Tashkent
    expect(formatDateTime('2026-10-09T20:30:00Z', 'Asia/Tashkent')).toBe('10.10.2026, 01:30')
    expect(formatDateTime('2026-10-09T20:30:00Z', 'UTC')).toBe('09.10.2026, 20:30')
  })

  it('knows what day it is in the shop', () => {
    const lateEveningUtc = new Date('2026-10-09T20:30:00Z')
    expect(todayIn('Asia/Tashkent', lateEveningUtc)).toBe('2026-10-10')
    expect(todayIn('UTC', lateEveningUtc)).toBe('2026-10-09')
  })

  it('builds report ranges', () => {
    expect(periodRange('month', '2026-10-10')).toEqual({ from: '2026-10-01', to: '2026-10-31' })
    expect(periodRange('prevMonth', '2026-10-10')).toEqual({ from: '2026-09-01', to: '2026-09-30' })
    expect(periodRange('prevMonth', '2026-01-15')).toEqual({ from: '2025-12-01', to: '2025-12-31' })
    expect(periodRange('quarter', '2026-11-20')).toEqual({ from: '2026-10-01', to: '2026-11-20' })
    expect(periodRange('quarter', '2026-02-03')).toEqual({ from: '2026-01-01', to: '2026-02-03' })
    expect(periodRange('year', '2026-10-10')).toEqual({ from: '2026-01-01', to: '2026-10-10' })
  })
})
