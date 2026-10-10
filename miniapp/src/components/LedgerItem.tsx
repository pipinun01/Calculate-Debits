import { Check, MessageSquare, Mic, Plus, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Transaction } from '../api/types'
import { formatDateTime } from '../lib/dates'
import { formatUzs } from '../lib/formatUzs'

interface Props {
  tx: Transaction
  timeZone: string
  onDelete?(tx: Transaction): void
}

/** One ledger line: debts in red with "+", payments in green with "−". */
export function LedgerItem({ tx, timeZone, onDelete }: Props) {
  const { t } = useTranslation()
  const debt = tx.type === 'debt'
  return (
    <article className="bg-card shadow-soft rounded-card p-3.5">
      <div className="flex items-center gap-3">
        <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${debt ? 'bg-danger-soft text-danger' : 'bg-success-soft text-success'}`}>
          {debt ? <Plus className="h-5 w-5" /> : <Check className="h-5 w-5" />}
        </div>
        <div className="min-w-0 flex-1">
          <p className="flex items-center gap-1.5 text-sm font-extrabold">
            {t(debt ? 'ledger.debt' : 'ledger.payment')}
            {tx.source === 'voice' && <Mic className="text-muted h-3 w-3" />}
          </p>
          <p className="text-muted truncate text-xs">
            {formatDateTime(tx.createdAt, timeZone)} · {tx.createdByName}
          </p>
        </div>
        <span className={`tabular shrink-0 rounded-xl px-2.5 py-1 text-sm font-extrabold ${debt ? 'bg-danger-soft text-danger' : 'bg-success-soft text-success'}`}>
          {debt ? '+' : '−'}
          {formatUzs(tx.amount)}
        </span>
      </div>
      {(tx.comment || (tx.canEdit && onDelete)) && (
        <div className="mt-2.5 flex items-center gap-2">
          {tx.comment ? (
            <p className="bg-field text-muted flex min-w-0 flex-1 items-center gap-2 rounded-xl px-3 py-2 text-xs italic">
              <MessageSquare className="h-3.5 w-3.5 shrink-0" />
              <span className="truncate">«{tx.comment}»</span>
            </p>
          ) : (
            <span className="flex-1" />
          )}
          {tx.canEdit && onDelete && (
            <button type="button" onClick={() => onDelete(tx)} aria-label={t('common.delete')} className="bg-danger-soft text-danger flex h-8 w-8 shrink-0 items-center justify-center rounded-xl">
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>
      )}
    </article>
  )
}
