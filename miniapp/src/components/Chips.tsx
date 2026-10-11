import type { ReactNode } from 'react'
import { haptic } from '../lib/telegram'

export interface ChipOption<T extends string> {
  value: T
  label: ReactNode
  tone?: 'default' | 'danger' | 'warning'
}

interface Props<T extends string> {
  options: ChipOption<NoInfer<T>>[]
  value: T
  onChange(value: T): void
  /** "pill" = main filters, "tag" = the quieter second-level filter. */
  shape?: 'pill' | 'tag'
}

/** Horizontally scrolling single-choice filter. */
export function FilterChips<T extends string>({ options, value, onChange, shape = 'pill' }: Props<T>) {
  return (
    <div className="no-scrollbar -mx-4 flex gap-1.5 overflow-x-auto px-4 py-0.5">
      {options.map((o) => {
        const active = o.value === value
        const idle =
          o.tone === 'danger'
            ? 'bg-danger-soft text-danger border-danger-line'
            : o.tone === 'warning'
              ? 'bg-warning-soft text-warning border-warning-line'
              : 'bg-card text-muted border-line'
        const on = shape === 'pill' ? 'bg-chip text-on-chip border-transparent font-bold shadow-soft' : 'bg-primary-tint text-accent border-primary-line font-bold'
        return (
          <button
            key={o.value}
            type="button"
            onClick={() => {
              haptic('select')
              onChange(o.value)
            }}
            className={`flex h-8 shrink-0 items-center gap-1.5 border px-3.5 text-xs font-semibold whitespace-nowrap transition active:scale-95 ${shape === 'pill' ? 'rounded-full' : 'rounded-lg px-3'} ${active ? on : idle}`}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

interface SegmentedProps<T extends string> {
  options: { value: NoInfer<T>; label: ReactNode; activeClass?: string }[]
  value: T
  onChange(value: T): void
  tone?: 'card' | 'primary'
}

/** Segmented control (tabs inside a pill). */
export function SegmentedTabs<T extends string>({ options, value, onChange, tone = 'card' }: SegmentedProps<T>) {
  const activeClass = tone === 'primary' ? 'bg-primary text-white shadow-elevated' : 'bg-card text-accent shadow-soft dark:bg-primary dark:text-white'
  return (
    <div className="bg-track/70 dark:border-line dark:bg-card flex gap-1 rounded-xl border border-transparent p-1">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          onClick={() => {
            haptic('select')
            onChange(o.value)
          }}
          className={`flex min-h-9 min-w-0 flex-1 items-center justify-center gap-1.5 rounded-lg px-2 py-1.5 text-center text-xs leading-tight transition ${o.value === value ? `font-bold ${o.activeClass ?? activeClass}` : 'text-muted font-semibold'}`}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}
