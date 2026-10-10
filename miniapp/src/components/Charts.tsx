import type { ReactNode } from 'react'
import { formatCompact } from '../lib/formatUzs'

interface Segment {
  value: number
  color: string
}

/** Ring chart with a label in the middle. Segments with zero value are skipped; an empty chart shows a grey ring. */
export function DonutChart({ segments, children, size = 190 }: { segments: Segment[]; children?: ReactNode; size?: number }) {
  const stroke = 22
  const r = (size - stroke) / 2
  const c = 2 * Math.PI * r
  const total = segments.reduce((sum, s) => sum + Math.max(0, s.value), 0)
  const gap = segments.filter((s) => s.value > 0).length > 1 ? 4 : 0
  let offset = 0
  return (
    <div className="relative mx-auto" style={{ width: size, height: size }}>
      <svg width={size} height={size} className="-rotate-90">
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--color-field)" strokeWidth={stroke} />
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

/** Paired bars per bucket (a = debts, b = payments), scaled to the tallest bar. */
export function BarChart({ bars, colorA, colorB }: { bars: Bar[]; colorA: string; colorB: string }) {
  const max = Math.max(1, ...bars.flatMap((x) => [x.a, x.b]))
  const height = 130
  return (
    <div className="flex items-end justify-between gap-2">
      {bars.map((bar, i) => (
        <div key={i} className="flex min-w-0 flex-1 flex-col items-center">
          <span className="text-muted tabular mb-1 h-3.5 text-[10px] font-bold">{bar.a || bar.b ? formatCompact(Math.max(bar.a, bar.b)) : ''}</span>
          <div className="flex w-full items-end justify-center gap-1" style={{ height }}>
            <div className="w-1/3 max-w-4 rounded-t-md" style={{ height: Math.max(3, (bar.a / max) * height), backgroundColor: colorA }} />
            <div className="w-1/3 max-w-4 rounded-t-md" style={{ height: Math.max(3, (bar.b / max) * height), backgroundColor: colorB }} />
          </div>
          <span className="text-muted mt-1.5 truncate text-[10px] font-bold">{bar.label}</span>
        </div>
      ))}
    </div>
  )
}

export function StatCard({ label, value, unit, children, tone }: { label: string; value: string; unit?: string; children?: ReactNode; tone?: 'success' | 'danger' }) {
  return (
    <div className="bg-card shadow-soft rounded-card min-w-0 flex-1 p-4">
      <p className="text-muted text-[11px] font-bold">{label}</p>
      <p className={`tabular mt-1 truncate text-lg font-extrabold ${tone === 'success' ? 'text-success' : tone === 'danger' ? 'text-danger' : ''}`}>
        {value}
        {unit && <span className="text-muted ml-1 text-[10px] font-bold">{unit}</span>}
      </p>
      {children}
    </div>
  )
}
