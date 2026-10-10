import { formatUzs, parseAmountInput } from '../lib/formatUzs'
import { haptic } from '../lib/telegram'

interface Props {
  value: number
  onChange(value: number): void
  label: string
  /** "+50 000" style increments. */
  steps?: number[]
  /** Optional "whole amount" chip (the full debt for a payment). */
  full?: { label: string; value: number }
  autoFocus?: boolean
}

/** Large amount field with thousands separators while typing, plus quick-add chips. */
export function AmountInput({ value, onChange, label, steps = [50_000, 100_000, 500_000], full, autoFocus }: Props) {
  return (
    <div className="bg-card shadow-soft rounded-card p-4">
      <p className="text-muted text-center text-[11px] font-extrabold tracking-widest uppercase">{label}</p>
      <div className="mt-1 flex items-baseline justify-center gap-2">
        <input
          inputMode="numeric"
          autoFocus={autoFocus}
          value={value ? formatUzs(value) : ''}
          onChange={(e) => onChange(parseAmountInput(e.target.value))}
          placeholder="0"
          aria-label={label}
          className="tabular placeholder:text-line w-full min-w-0 bg-transparent text-center text-4xl font-extrabold outline-none"
          style={{ maxWidth: `${Math.max(2, formatUzs(value).length + 1)}ch` }}
        />
        <span className="text-primary text-sm font-extrabold">UZS</span>
      </div>
      <div className="no-scrollbar -mx-1 mt-3 flex gap-2 overflow-x-auto px-1">
        {steps.map((step) => (
          <button
            key={step}
            type="button"
            onClick={() => {
              haptic('tap')
              onChange(Math.min(value + step, 1_000_000_000_000))
            }}
            className="border-line bg-card tabular h-9 shrink-0 rounded-xl border px-3 text-xs font-bold active:bg-field"
          >
            +{formatUzs(step)}
          </button>
        ))}
        {full && full.value > 0 && (
          <button
            type="button"
            onClick={() => {
              haptic('tap')
              onChange(full.value)
            }}
            className="bg-primary-soft text-primary h-9 shrink-0 rounded-xl px-3 text-xs font-bold"
          >
            {full.label}
          </button>
        )}
      </div>
    </div>
  )
}
