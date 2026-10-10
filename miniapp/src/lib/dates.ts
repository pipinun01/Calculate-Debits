import dayjs from 'dayjs'

/** Calendar dates travel as "YYYY-MM-DD" strings (shop-local); only timestamps are real instants. */
export type IsoDate = string

export const toIso = (d: dayjs.Dayjs): IsoDate => d.format('YYYY-MM-DD')

export const addDays = (date: IsoDate, days: number): IsoDate => toIso(dayjs(date).add(days, 'day'))

export const addMonths = (date: IsoDate, months: number): IsoDate => toIso(dayjs(date).add(months, 'month'))

/** Last day of the month; if today already is the last day, the end of the next month (same rule as the bot). */
export function endOfMonth(today: IsoDate): IsoDate {
  const end = dayjs(today).endOf('month')
  return toIso(end.isAfter(dayjs(today), 'day') ? end : dayjs(today).add(1, 'month').endOf('month'))
}

export const daysBetween = (from: IsoDate, to: IsoDate): number => dayjs(to).diff(dayjs(from), 'day')

/** "2026-10-16" → "16.10.2026" */
export const formatDate = (date: IsoDate): string => dayjs(date).format('DD.MM.YYYY')

/** "2026-10-16" → "16.10" */
export const formatDayMonth = (date: IsoDate): string => dayjs(date).format('DD.MM')

/** UTC instant → "16.10.2026, 14:30" in the shop's time zone. */
export function formatDateTime(iso: string, timeZone: string): string {
  const parts = new Intl.DateTimeFormat('ru-RU', {
    timeZone,
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(new Date(iso))
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? ''
  return `${get('day')}.${get('month')}.${get('year')}, ${get('hour')}:${get('minute')}`
}

/** Today's calendar date in a time zone. */
export function todayIn(timeZone: string, now: Date = new Date()): IsoDate {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now)
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? ''
  return `${get('year')}-${get('month')}-${get('day')}`
}

export type ReportPeriod = 'month' | 'prevMonth' | 'quarter' | 'year'

/** Inclusive date range of a report period relative to today. */
export function periodRange(period: ReportPeriod, today: IsoDate): { from: IsoDate; to: IsoDate } {
  const d = dayjs(today)
  switch (period) {
    case 'prevMonth': {
      const prev = d.subtract(1, 'month')
      return { from: toIso(prev.startOf('month')), to: toIso(prev.endOf('month')) }
    }
    case 'quarter': {
      const startMonth = Math.floor(d.month() / 3) * 3
      return { from: toIso(d.month(startMonth).startOf('month')), to: today }
    }
    case 'year':
      return { from: toIso(d.startOf('year')), to: today }
    default:
      return { from: toIso(d.startOf('month')), to: toIso(d.endOf('month')) }
  }
}
