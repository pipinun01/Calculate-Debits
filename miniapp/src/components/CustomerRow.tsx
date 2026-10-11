import { Check } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import type { CustomerListItem } from '../api/types'
import { daysBetween } from '../lib/dates'
import { formatUzs } from '../lib/formatUzs'
import { Avatar } from './Avatar'
import { DueBadge } from './DueBadge'

interface Props {
  customer: CustomerListItem
  today: string
}

export function CustomerRow({ customer: c, today }: Props) {
  const { t } = useTranslation()
  const settled = c.balance === 0
  return (
    <Link to={`/customers/${c.id}`} className="bg-card border-line shadow-soft rounded-card active:bg-elevated flex items-center gap-3 border p-3.5 transition active:scale-[0.99]">
      <Avatar name={c.name} seed={c.id} />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm leading-tight font-bold">
          {c.name}
          {c.tag && <span className="text-muted font-medium"> ({c.tag})</span>}
        </p>
        {c.phone && <p className="text-muted tabular mt-0.5 truncate text-[11px] font-medium">{c.phone}</p>}
        <div className="mt-1.5 flex">
          {c.nextDueDate ? (
            <DueBadge daysLeft={daysBetween(today, c.nextDueDate)} dueDate={c.nextDueDate} />
          ) : (
            settled && (
              <span className="bg-success-soft text-success border-success-line inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-[11px] font-semibold">
                <Check className="h-3 w-3" />
                {t('customers.paid')}
              </span>
            )
          )}
        </div>
      </div>
      <div className="shrink-0 text-right">
        <p className={`tabular text-sm font-extrabold tracking-tight ${c.balance < 0 ? 'text-success' : settled ? 'text-muted' : ''}`}>{formatUzs(c.balance)}</p>
        <p className="text-muted text-[10px] font-semibold">UZS</p>
      </div>
    </Link>
  )
}
