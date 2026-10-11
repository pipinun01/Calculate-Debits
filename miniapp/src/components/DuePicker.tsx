import { Calendar, Check } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { addDays, endOfMonth, formatDate } from '../lib/dates'
import { haptic } from '../lib/telegram'

interface Props {
  today: string
  value: string
  onChange(date: string): void
}

/** Term presets (3 days / week / month / end of month) and a native date picker for anything else. */
export function DuePicker({ today, value, onChange }: Props) {
  const { t } = useTranslation()
  const presets = [
    { label: t('due.preset.3'), date: addDays(today, 3) },
    { label: t('due.preset.7'), date: addDays(today, 7) },
    // 30 days, not a calendar month: the same as the bot's button and the usual shop default.
    { label: t('due.preset.30'), date: addDays(today, 30) },
    { label: t('due.preset.eom'), date: endOfMonth(today) },
  ]
  const isPreset = presets.some((p) => p.date === value)
  const on = 'border-primary bg-primary text-white shadow-elevated'
  return (
    <div>
      <div className="mb-2 flex items-center justify-between gap-2">
        <span className="text-muted text-[11px] font-bold tracking-wider uppercase">{t('entry.due')}</span>
        <span className="text-accent bg-primary-tint border-primary-line tabular rounded-md border px-2 py-0.5 text-xs font-bold">{formatDate(value)}</span>
      </div>
      <div className="flex flex-wrap gap-2">
        {presets.map((p) => {
          const active = p.date === value
          return (
            <button
              key={p.label}
              type="button"
              onClick={() => {
                haptic('select')
                onChange(p.date)
              }}
              className={`flex h-9 items-center gap-1.5 rounded-xl border px-3.5 text-xs transition active:scale-95 ${active ? `font-bold ${on}` : 'border-line bg-field font-semibold'}`}
            >
              {p.label}
              {active && <Check className="h-3.5 w-3.5" />}
            </button>
          )
        })}
        <label className={`relative flex h-9 items-center gap-1.5 rounded-xl border px-3.5 text-xs transition ${!isPreset ? `font-bold ${on}` : 'border-line text-muted border-dashed font-semibold'}`}>
          <Calendar className="h-3.5 w-3.5" />
          {t('due.preset.other')}
          <input
            type="date"
            min={today}
            value={value}
            onChange={(e) => e.target.value && e.target.value >= today && onChange(e.target.value)}
            className="absolute inset-0 h-full w-full opacity-0"
            aria-label={t('due.preset.other')}
          />
        </label>
      </div>
    </div>
  )
}
