import type { ReactNode } from 'react'
import { formatCompact } from '../lib/formatUzs'

interface Segment {
  value: number
  color: string
}

/** Ring chart with a label in the middle. Segments with zero value are skipped; an empty chart shows a grey ring. */
export function DonutChart({ segments, children, size = 176 }: { segments: Segment[]; children?: ReactNode; size?: number }) {
  const stroke = 20
  const r = (size - stroke) / 2
  const c = 2 * Math.PI * r
  const total = segments.reduce((sum, s) => sum + Math.max(0, s.value), 0)
  const gap = segments.filter((s) => s.value > 0).length > 1 ? 4 : 0
  let offset = 0
  return (
    <div className="relative mx-auto shrink-0" style={{ width: size, height: size }}>
      <svg width={size} height={size} className="-rotate-90">
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--color-track)" strokeWidth={stroke} />
        {total > 0 &&
          segments.map((s, i) => {
            if (s.value <= 0) return null
            const length = (s.value / total) * c
            const dash = Math.max(length - gap, 1)
            const el = (
              <circle
                key={i}
                cx={size / 2}
                cy={size / 2}
                r={r}
                fill="none"
                stroke={s.color}
                strokeWidth={stroke}
                strokeLinecap="round"
                strokeDasharray={`${dash} ${c - dash}`}
                strokeDashoffset={-offset}
              />
            )
            offset += length
            return el
          })}
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center text-center">{children}</div>
    </div>
  )
}

interface Bar {
  label: string
  a: number
  b: number
}

/** Paired bars per bucket, scaled to the tallest bar; the last bucket (the current one) is highlighted. */
export function BarChart({ bars, colorA, colorB }: { bars: Bar[]; colorA: string; colorB: string }) {
  const max = Math.max(1, ...bars.flatMap((x) => [x.a, x.b]))
  const height = 128
  return (
    <div className="flex items-end justify-between gap-2 px-1 md:gap-4">
      {bars.map((bar, i) => {
        const last = i === bars.length - 1
        return (
          <div key={i} className="flex min-w-0 flex-1 flex-col items-center">
            <span className="text-muted tabular mb-1.5 h-4 text-[11px] font-semibold">{bar.a || bar.b ? formatCompact(Math.max(bar.a, bar.b)) : ''}</span>
            <div className="flex w-full items-end justify-center gap-1.5" style={{ height }}>
              <div className="w-3.5 max-w-[38%] rounded-t-lg transition-all duration-300" style={{ height: Math.max(3, (bar.a / max) * height), backgroundColor: colorA }} />
              <div className="w-3.5 max-w-[38%] rounded-t-lg transition-all duration-300" style={{ height: Math.max(3, (bar.b / max) * height), backgroundColor: colorB }} />
            </div>
            <span className={`mt-2 max-w-full truncate text-[11px] ${last ? 'text-accent font-bold' : 'text-muted font-medium'}`}>{bar.label}</span>
          </div>
        )
      })}
    </div>
  )
}

/** Small tile inside a report card. */
export function StatCard({ label, value, unit, children, tone, right }: { label: string; value: string; unit?: string; children?: ReactNode; tone?: 'success' | 'danger'; right?: ReactNode }) {
  return (
    <div className="bg-field border-line min-w-0 flex-1 rounded-2xl border p-3">
      <div className="flex items-center justify-between gap-2">
        <p className="text-muted truncate text-xs font-medium">{label}</p>
        {right}
      </div>
      <p className={`tabular mt-1 truncate text-sm font-bold ${tone === 'success' ? 'text-success' : tone === 'danger' ? 'text-danger' : ''}`}>
        {value}
        {unit && <span className="text-muted ml-1 text-[11px] font-normal">{unit}</span>}
      </p>
      {children}
    </div>
  )
}

export function ProgressBar({ value, color }: { value: number; color: string }) {
  return (
    <div className="bg-track mt-2 h-1.5 overflow-hidden rounded-full">
      <div className="h-full rounded-full" style={{ width: `${Math.min(100, Math.max(0, Math.round(value * 100)))}%`, backgroundColor: color }} />
    </div>
  )
}
