import { useTranslation } from 'react-i18next'
import { formatDayMonth } from '../lib/dates'

interface Props {
  /** Days until the due date; negative = overdue. */
  daysLeft: number
  dueDate: string
}

/** Red when overdue, amber within 3 days, blue within a week, neutral otherwise. */
export function DueBadge({ daysLeft, dueDate }: Props) {
  const { t } = useTranslation()
  const [text, tone, dot] =
    daysLeft < 0
      ? [t('due.overdue', { days: -daysLeft }), 'bg-danger-soft text-danger border-danger-line', 'bg-danger animate-pulse']
      : daysLeft === 0
        ? [t('due.today'), 'bg-warning-soft text-warning border-warning-line', 'bg-warning-dot']
        : daysLeft <= 3
          ? [t('due.in', { days: daysLeft }), 'bg-warning-soft text-warning border-warning-line', 'bg-warning-dot']
          : daysLeft <= 7
            ? [t('due.in', { days: daysLeft }), 'bg-primary-tint text-accent border-primary-line', 'bg-accent']
            : [t('due.on', { date: formatDayMonth(dueDate) }), 'bg-field text-muted border-line', 'bg-muted']
  return (
    <span className={`inline-flex max-w-full items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-semibold whitespace-nowrap ${tone}`}>
      <span className={`h-1.5 w-1.5 shrink-0 rounded-full ${dot}`} />
      <span className="truncate">{text}</span>
    </span>
  )
}
