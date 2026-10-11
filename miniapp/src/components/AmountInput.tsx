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
    <div className="border-primary-line from-primary-tint to-field rounded-2xl border bg-linear-to-b p-4">
      <p className="text-muted text-center text-[11px] font-bold tracking-wider uppercase">{label}</p>
      <div className="mt-1 flex items-baseline justify-center gap-2">
        <input
          inputMode="numeric"
          autoFocus={autoFocus}
          value={value ? formatUzs(value) : ''}
          onChange={(e) => onChange(parseAmountInput(e.target.value))}
          placeholder="0"
          aria-label={label}
          className="tabular placeholder:text-muted/40 caret-accent w-full min-w-0 bg-transparent text-right text-4xl font-extrabold tracking-tight outline-none md:text-5xl"
          style={{ maxWidth: `${Math.max(2, formatUzs(value).length + 0.5)}ch` }}
        />
        <span className="text-accent text-lg font-bold">UZS</span>
      </div>
      <div className="no-scrollbar -mx-1 mt-3 flex gap-1.5 overflow-x-auto px-1">
        {steps.map((step) => (
          <button
            key={step}
            type="button"
            onClick={() => {
              haptic('tap')
              onChange(Math.min(value + step, 1_000_000_000_000))
            }}
            className="border-line bg-card tabular active:border-primary-line active:bg-primary-tint h-9 shrink-0 rounded-xl border px-3 text-xs font-semibold shadow-sm transition active:scale-95"
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
            className="bg-primary-soft text-accent border-primary-line h-9 shrink-0 rounded-xl border px-3 text-xs font-bold transition active:scale-95"
          >
            {full.label}
          </button>
        )}
      </div>
    </div>
  )
}
