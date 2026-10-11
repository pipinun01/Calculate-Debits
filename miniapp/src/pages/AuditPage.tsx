import { FileSpreadsheet, History, ReceiptText, Store, UserRound, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Navigate } from 'react-router-dom'
import { useAudit, useShop } from '../api/queries'
import type { AuditEvent } from '../api/types'
import { useSession } from '../auth/Session'
import { PageHeader } from '../components/ShopHeader'
import { Empty, ErrorState, Loading } from '../components/States'
import { useBackButton } from '../hooks/useBackButton'
import { useInfiniteScroll } from '../hooks/useInfiniteScroll'
import { formatDateTime } from '../lib/dates'
import { formatUzs } from '../lib/formatUzs'

function icon(event: AuditEvent) {
  const cls = 'h-4 w-4'
  if (event.action.startsWith('export.')) return <FileSpreadsheet className={cls} />
  switch (event.entity) {
    case 'transaction':
      return <ReceiptText className={cls} />
    case 'customer':
      return <UserRound className={cls} />
    case 'member':
    case 'invite':
      return <Users className={cls} />
    default:
      return <Store className={cls} />
  }
}

/** Owner's view of who did what in the shop, newest first. */
export default function AuditPage() {
  const { t } = useTranslation()
  const { shop: membership, isOwner } = useSession()
  useBackButton(true)

  const shop = useShop()
  const audit = useAudit(isOwner)
  const events = audit.data?.pages.flatMap((p) => p.items) ?? []
  const sentinel = useInfiniteScroll<HTMLDivElement>(Boolean(audit.hasNextPage) && !audit.isFetchingNextPage, () => void audit.fetchNextPage())

  if (!isOwner) return <Navigate to="/settings" replace />

  const details = (e: AuditEvent) =>
    [e.subject, e.transactionType && e.amount !== null ? `${t(`ledger.${e.transactionType}`)} · ${formatUzs(e.amount)} UZS` : null].filter(Boolean).join(' · ')

  return (
    <div className="pb-8">
      <PageHeader title={t('audit.title')} subtitle={membership?.shopName} />
      <div className="app-x pt-1">
        {audit.isPending || shop.isPending ? (
          <Loading full />
        ) : audit.isError || shop.isError ? (
          <ErrorState onRetry={() => void audit.refetch()} />
        ) : events.length === 0 ? (
          <Empty icon={<History className="h-6 w-6" />} title={t('audit.empty')} hint={t('audit.emptyHint')} />
        ) : (
          <div className="bg-card border-line shadow-soft rounded-card border mt-2 overflow-hidden">
            {events.map((e) => (
              <div key={e.id} className="border-line flex items-start gap-3 border-b px-4 py-3 last:border-b-0">
                <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${e.action.endsWith('.delete') || e.action === 'member.deactivate' ? 'bg-danger-soft text-danger' : 'bg-primary-soft text-accent'}`}>{icon(e)}</div>
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-bold">{t(`audit.action.${e.action}`, { defaultValue: e.action })}</p>
                  {details(e) && <p className="truncate text-xs">{details(e)}</p>}
                  <p className="text-muted mt-0.5 text-[11px]">
                    {e.memberName ?? t('audit.system')} · <span className="tabular">{formatDateTime(e.createdAt, shop.data.timezone)}</span>
                  </p>
                </div>
              </div>
            ))}
            <div ref={sentinel} />
            {audit.isFetchingNextPage && <Loading />}
          </div>
        )}
      </div>
    </div>
  )
}
