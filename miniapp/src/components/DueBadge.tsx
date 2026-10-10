import { useTranslation } from 'react-i18next'
import { formatDayMonth } from '../lib/dates'

interface Props {
  /** Days until the due date; negative = overdue. */
  daysLeft: number
  dueDate: string
}

/** Red when overdue, amber within 3 days, neutral otherwise. */
export function DueBadge({ daysLeft, dueDate }: Props) {
  const { t } = useTranslation()
  const [text, tone] =
    daysLeft < 0
      ? [t('due.overdue', { days: -daysLeft }), 'bg-danger-soft text-danger']
      : daysLeft === 0
        ? [t('due.today'), 'bg-warning-soft text-warning']
        : daysLeft <= 3
          ? [t('due.in', { days: daysLeft }), 'bg-warning-soft text-warning']
          : [t('due.on', { date: formatDayMonth(dueDate) }), 'bg-field text-muted']
  return (
    <span className={`inline-flex items-center gap-1 rounded-lg px-2 py-0.5 text-[11px] font-bold whitespace-nowrap ${tone}`}>
      {daysLeft < 0 && <span className="bg-danger h-1.5 w-1.5 rounded-full" />}
      {text}
    </span>
  )
}
