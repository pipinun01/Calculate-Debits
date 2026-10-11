import { CircleCheck, CirclePlus, Mic, ShoppingBag, Trash2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { Transaction } from '../api/types'
import { formatDateTime } from '../lib/dates'
import { formatUzs } from '../lib/formatUzs'

interface Props {
  tx: Transaction
  timeZone: string
  onDelete?(tx: Transaction): void
}

const tones = {
  danger: 'bg-danger-soft text-danger border-danger-line',
  warning: 'bg-warning-soft text-warning border-warning-line',
  success: 'bg-success-soft text-success border-success-line',
}

/** One ledger line: debts in red with "+", payments in green with "−", and how much of a debt is still open. */
export function LedgerItem({ tx, timeZone, onDelete }: Props) {
  const { t } = useTranslation()
  const debt = tx.type === 'debt'
  const remaining = tx.remaining ?? tx.amount
  const [status, tone] = !debt
    ? [t('ledger.status.accepted'), tones.success]
    : remaining <= 0
      ? [t('ledger.status.closed'), tones.success]
      : remaining < tx.amount
        ? [t('ledger.status.partial'), tones.warning]
        : [t('ledger.status.open'), tones.danger]
  const canDelete = tx.canEdit && onDelete

  return (
    <article className="bg-card border-line shadow-soft rounded-card flex flex-col gap-3 border p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-1 items-start gap-3">
          <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full border ${debt ? tones.danger : tones.success}`}>
            {debt ? <CirclePlus className="h-5 w-5" /> : <CircleCheck className="h-5 w-5" />}
          </div>
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span className="flex items-center gap-1.5 text-sm font-bold">
                {t(debt ? 'ledger.debt' : 'ledger.payment')}
                {tx.source === 'voice' && <Mic className="text-muted h-3 w-3" />}
              </span>
              <span className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold whitespace-nowrap ${tone}`}>{status}</span>
            </div>
            <p className="text-muted tabular mt-0.5 text-xs">{formatDateTime(tx.createdAt, timeZone)}</p>
          </div>
        </div>
        <div className="shrink-0 text-right">
          <p className={`tabular text-base leading-none font-extrabold tracking-tight ${debt ? 'text-danger' : 'text-success'}`}>
            {debt ? '+' : '−'}
            {formatUzs(tx.amount)}
          </p>
          <p className={`mt-1 text-[11px] font-bold opacity-70 ${debt ? 'text-danger' : 'text-success'}`}>UZS</p>
        </div>
      </div>

      <div className={`flex flex-col gap-2 rounded-xl border p-3 text-xs ${debt ? 'bg-field border-line' : 'bg-success-soft border-success-line'}`}>
        {tx.comment && (
          <p className={`flex items-start gap-1.5 leading-relaxed font-medium ${debt ? '' : 'text-success'}`}>
            {debt ? <ShoppingBag className="text-muted mt-0.5 h-3.5 w-3.5 shrink-0" /> : <span className="bg-success mt-1.5 h-2 w-2 shrink-0 rounded-full" />}
            <span className="line-clamp-2 min-w-0 break-words">{tx.comment}</span>
          </p>
        )}
        <div className={`flex items-center justify-between gap-2 ${debt ? 'text-muted border-line' : 'text-success border-success-line'} ${tx.comment ? 'border-t pt-2' : ''}`}>
          <span className="truncate">{t(debt ? 'ledger.writtenBy' : 'ledger.acceptedBy', { name: tx.createdByName })}</span>
          {debt && remaining > 0 && remaining < tx.amount && <span className="tabular shrink-0 font-semibold">{t('ledger.left', { amount: formatUzs(remaining) })}</span>}
          {canDelete && (
            <button type="button" onClick={() => onDelete(tx)} aria-label={t('common.delete')} className="bg-danger-soft text-danger border-danger-line -my-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border">
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>
      </div>
    </article>
  )
}
