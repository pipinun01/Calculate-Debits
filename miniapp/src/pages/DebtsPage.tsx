import { useState } from 'react'
import { Bell, CircleCheck, Phone } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { useDebts, useShop } from '../api/queries'
import type { DebtStatus, OpenDebt } from '../api/types'
import { useSession } from '../auth/Session'
import { DonutChart } from '../components/Charts'
import { FilterChips } from '../components/Chips'
import { DueBadge } from '../components/DueBadge'
import { ShopHeader } from '../components/ShopHeader'
import { Empty, ErrorState, Loading, SectionTitle } from '../components/States'
import { useInfiniteScroll } from '../hooks/useInfiniteScroll'
import { formatCompact, formatUzs } from '../lib/formatUzs'
import { telLink } from '../lib/people'
import { reminderText } from '../lib/reminder'
import { shareToChat } from '../lib/telegram'

const COLORS = { overdue: '#ef4444', dueSoon: '#f59e0b', onTime: '#8b5cf6' }

export default function DebtsPage() {
  const { t } = useTranslation()
  const { shop: membership } = useSession()
  const shop = useShop()
  const [status, setStatus] = useState<DebtStatus>('open')
  const debts = useDebts(status)
  const summary = debts.data?.pages[0]?.summary
  const items = debts.data?.pages.flatMap((p) => p.items) ?? []
  const sentinel = useInfiniteScroll<HTMLDivElement>(Boolean(debts.hasNextPage) && !debts.isFetchingNextPage, () => void debts.fetchNextPage())

  const remind = (d: OpenDebt) =>
    shareToChat(
      reminderText({
        language: shop.data?.language ?? 'uz-Latn',
        customerName: d.customerName,
        shopName: membership?.shopName ?? '',
        amount: d.remaining,
        dueDate: d.dueDate,
        daysLeft: d.daysLeft,
      })
    )

  const rows = summary
    ? [
        { key: 'overdue', label: t('debts.overdue'), value: summary.overdue, count: summary.overdueCount, color: COLORS.overdue, bg: 'bg-danger-soft' },
        { key: 'dueSoon', label: t('debts.dueSoon'), value: summary.dueSoon, count: summary.dueSoonCount, color: COLORS.dueSoon, bg: 'bg-warning-soft' },
        { key: 'onTime', label: t('debts.onTime'), value: summary.onTime, count: null, color: COLORS.onTime, bg: 'bg-primary-tint' },
      ]
    : []

  return (
    <>
      <ShopHeader />
      <div className="px-4">
        <h1 className="px-1 text-xl font-extrabold">{t('debts.title')}</h1>
        <p className="text-muted px-1 text-xs">{t('debts.subtitle')}</p>

        {debts.isPending ? (
          <Loading full />
        ) : debts.isError || !summary ? (
          <ErrorState onRetry={() => void debts.refetch()} />
        ) : (
          <>
            <section className="bg-card shadow-soft rounded-card mt-3 p-4">
              <DonutChart
                segments={[
                  { value: summary.overdue, color: COLORS.overdue },
                  { value: summary.dueSoon, color: COLORS.dueSoon },
                  { value: summary.onTime, color: COLORS.onTime },
                ]}
              >
                <p className="text-muted text-[10px] font-extrabold tracking-widest uppercase">{t('debts.total')}</p>
                <p className="tabular text-2xl font-extrabold">{formatCompact(summary.total)}</p>
                <span className="bg-primary-soft text-primary mt-1 rounded-md px-1.5 py-0.5 text-[10px] font-bold">UZS</span>
              </DonutChart>
              <div className="mt-4 space-y-2">
                {rows.map((r) => (
                  <div key={r.key} className={`flex items-center gap-2 rounded-xl px-3 py-2.5 ${r.bg}`}>
                    <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ backgroundColor: r.color }} />
                    <span className="min-w-0 flex-1 truncate text-xs font-bold">{r.label}</span>
                    <span className="tabular text-xs font-extrabold">{formatUzs(r.value)} UZS</span>
                    {r.count !== null && <span className="bg-card text-muted rounded-md px-1.5 py-0.5 text-[10px] font-bold">{t('debts.people', { count: r.count })}</span>}
                  </div>
                ))}
              </div>
            </section>

            <div className="mt-4">
              <FilterChips<DebtStatus>
                value={status}
                onChange={setStatus}
                options={[
                  { value: 'open', label: `${t('customers.filter.all')} (${summary.debtorsCount})` },
                  { value: 'overdue', label: `${t('debts.overdue')} (${summary.overdueCount})`, tone: 'danger' },
                  { value: 'due_soon', label: `${t('debts.dueSoon')} (${summary.dueSoonCount})`, tone: 'warning' },
                  { value: 'on_time', label: t('debts.onTime') },
                ]}
              />
            </div>

            <SectionTitle>{t(`debts.section.${status}`)}</SectionTitle>
            {items.length === 0 ? (
              <Empty icon={<CircleCheck className="h-6 w-6" />} title={t('debts.empty')} />
            ) : (
              <div className="space-y-2.5">
                {items.map((d) => {
                  const tel = telLink(d.customerPhone)
                  const accent = d.daysLeft < 0 ? 'border-l-danger' : d.daysLeft <= 3 ? 'border-l-warning' : 'border-l-primary-light'
                  return (
                    <article key={d.debtId} className={`bg-card shadow-soft rounded-card border-l-4 p-3.5 ${accent}`}>
                      <Link to={`/customers/${d.customerId}`} className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-extrabold">
                            {d.customerName}
                            {d.customerTag && <span className="text-muted font-semibold"> ({d.customerTag})</span>}
                          </p>
                          <div className="mt-1.5">
                            <DueBadge daysLeft={d.daysLeft} dueDate={d.dueDate} />
                          </div>
                        </div>
                        <div className="shrink-0 text-right">
                          <p className="tabular text-sm font-extrabold">{formatUzs(d.remaining)}</p>
                          <p className="text-muted text-[10px] font-bold">UZS</p>
                        </div>
                      </Link>
                      <div className="border-line mt-3 flex justify-end gap-2 border-t pt-3">
                        {tel && (
                          <a href={tel} aria-label={t('customer.call')} className="bg-field text-text flex h-9 w-9 items-center justify-center rounded-xl">
                            <Phone className="h-4 w-4" />
                          </a>
                        )}
                        <button type="button" onClick={() => remind(d)} className={`flex h-9 items-center gap-1.5 rounded-xl px-3 text-xs font-bold ${d.daysLeft < 0 ? 'bg-danger-soft text-danger' : 'bg-primary-soft text-primary'}`}>
                          <Bell className="h-3.5 w-3.5" />
                          {t('debts.remind')}
                        </button>
                      </div>
                    </article>
                  )
                })}
                <div ref={sentinel} />
                {debts.isFetchingNextPage && <Loading />}
              </div>
            )}
          </>
        )}
      </div>
    </>
  )
}
