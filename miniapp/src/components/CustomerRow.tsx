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
  return (
    <Link to={`/customers/${c.id}`} className="bg-card shadow-soft rounded-card flex items-center gap-3 p-3.5 transition active:scale-[0.99]">
      <Avatar name={c.name} seed={c.id} />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-extrabold">
          {c.name}
          {c.tag && <span className="text-muted font-semibold"> ({c.tag})</span>}
        </p>
        {c.phone && <p className="text-muted tabular truncate text-xs">{c.phone}</p>}
        {c.nextDueDate && (
          <div className="mt-1.5">
            <DueBadge daysLeft={daysBetween(today, c.nextDueDate)} dueDate={c.nextDueDate} />
          </div>
        )}
      </div>
      <div className="shrink-0 text-right">
        <p className={`tabular text-sm font-extrabold ${c.balance < 0 ? 'text-success' : ''}`}>{formatUzs(c.balance)}</p>
        <p className="text-muted text-[10px] font-bold">UZS</p>
      </div>
    </Link>
  )
}
