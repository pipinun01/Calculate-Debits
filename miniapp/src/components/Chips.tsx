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
}

/** Horizontally scrolling single-choice filter. */
export function FilterChips<T extends string>({ options, value, onChange }: Props<T>) {
  return (
    <div className="no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 py-1">
      {options.map((o) => {
        const active = o.value === value
        const idle =
          o.tone === 'danger'
            ? 'bg-danger-soft text-danger'
            : o.tone === 'warning'
              ? 'bg-warning-soft text-warning'
              : 'bg-card text-text border border-line'
        return (
          <button
            key={o.value}
            type="button"
            onClick={() => {
              haptic('select')
              onChange(o.value)
            }}
            className={`h-9 shrink-0 rounded-full px-4 text-xs font-bold whitespace-nowrap transition ${active ? 'bg-dark-card text-white dark:bg-primary' : idle}`}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

interface SegmentedProps<T extends string> {
  options: { value: NoInfer<T>; label: ReactNode }[]
  value: T
  onChange(value: T): void
  tone?: 'card' | 'primary'
}

/** Segmented control (tabs inside a pill). */
export function SegmentedTabs<T extends string>({ options, value, onChange, tone = 'card' }: SegmentedProps<T>) {
  const activeClass = tone === 'primary' ? 'bg-primary text-white shadow-soft' : 'bg-card text-text shadow-soft'
  return (
    <div className="bg-field flex rounded-2xl p-1">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          onClick={() => {
            haptic('select')
            onChange(o.value)
          }}
          className={`flex h-10 flex-1 items-center justify-center gap-1.5 rounded-xl px-2 text-xs font-bold transition ${o.value === value ? activeClass : 'text-muted'}`}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}
