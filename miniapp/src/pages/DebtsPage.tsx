import { useState } from 'react'
import { BellRing, CircleCheck, Phone } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { useDebts, useShop } from '../api/queries'
import type { DebtStatus, OpenDebt } from '../api/types'
import { useSession } from '../auth/Session'
import { DonutChart } from '../components/Charts'
import { FilterChips } from '../components/Chips'
import { DueBadge } from '../components/DueBadge'
import { PageTitle, ShopHeader } from '../components/ShopHeader'
import { Empty, ErrorState, Loading } from '../components/States'
import { useInfiniteScroll } from '../hooks/useInfiniteScroll'
import { formatCompact, formatUzs } from '../lib/formatUzs'
import { telLink } from '../lib/people'
import { reminderText } from '../lib/reminder'
import { shareToChat } from '../lib/telegram'

type Zone = 'overdue' | 'due_soon' | 'on_time'

const zoneOf = (d: OpenDebt): Zone => (d.daysLeft < 0 ? 'overdue' : d.daysLeft <= 3 ? 'due_soon' : 'on_time')

const ZONES: Record<Zone, { color: string; title: string; dot: string; row: string; stripe: string; card: string; action: string }> = {
  overdue: {
    color: 'var(--color-danger-fill)',
    title: 'text-danger',
    dot: 'bg-danger',
    row: 'bg-danger-soft border-danger-line',
    stripe: 'bg-danger-fill',
    card: 'border-danger-line',
    action: 'bg-danger-soft text-danger border-danger-line',
  },
  due_soon: {
    color: 'var(--color-warning-dot)',
    title: 'text-warning',
    dot: 'bg-warning-dot',
    row: 'bg-warning-soft border-warning-line',
    stripe: 'bg-warning-dot',
    card: 'border-warning-line',
    action: 'bg-warning-soft text-warning border-warning-line',
  },
  on_time: {
    color: 'var(--color-chart)',
    title: 'text-accent',
    dot: 'bg-chart',
    row: 'bg-field border-line',
    stripe: 'bg-chart',
    card: 'border-line',
    action: 'bg-primary-tint text-accent border-primary-line',
  },
}

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
        { zone: 'overdue' as const, label: t('debts.overdue'), value: summary.overdue, count: summary.overdueCount },
        { zone: 'due_soon' as const, label: t('debts.dueSoon'), value: summary.dueSoon, count: summary.dueSoonCount },
        { zone: 'on_time' as const, label: t('debts.onTime'), value: summary.onTime, count: null },
      ]
    : []

  // One section per zone, in the order of urgency; the list itself arrives sorted by due date.
  const groups = (['overdue', 'due_soon', 'on_time'] as const).map((zone) => ({ zone, debts: items.filter((d) => zoneOf(d) === zone) })).filter((g) => g.debts.length > 0)
  const counts: Record<Zone, number | null> = { overdue: summary?.overdueCount ?? null, due_soon: summary?.dueSoonCount ?? null, on_time: null }

  return (
    <>
      <ShopHeader />
      <div className="app-x">
        <PageTitle title={t('debts.title')} subtitle={t('debts.subtitle')} />

        {debts.isPending ? (
          <Loading full />
        ) : debts.isError || !summary ? (
          <ErrorState onRetry={() => void debts.refetch()} />
        ) : (
          <>
            <section className="border-primary-line from-primary-tint to-card shadow-soft mt-4 flex flex-col items-center gap-4 rounded-3xl border bg-linear-to-b p-5 md:flex-row md:gap-8 md:p-6">
              <DonutChart segments={rows.map((r) => ({ value: r.value, color: ZONES[r.zone].color }))}>
                <p className="text-muted text-[11px] font-semibold tracking-wider uppercase">{t('debts.total')}</p>
                <p className="tabular text-xl font-extrabold tracking-tight">{formatCompact(summary.total)}</p>
                <span className="bg-primary-soft text-accent mt-0.5 rounded px-1.5 py-0.5 text-[10px] font-bold">UZS</span>
              </DonutChart>
              <div className="w-full min-w-0 space-y-2">
                {rows.map((r) => (
                  <div key={r.zone} className={`flex flex-wrap items-center justify-between gap-x-3 gap-y-1 rounded-2xl border p-2.5 ${ZONES[r.zone].row}`}>
                    <div className="flex min-w-0 items-center gap-2">
                      <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${ZONES[r.zone].dot}`} />
                      <span className="truncate text-xs font-medium">{r.label}</span>
                    </div>
                    <div className="flex items-center gap-1.5">
                      <span className={`tabular text-xs font-bold whitespace-nowrap ${r.zone === 'on_time' ? '' : ZONES[r.zone].title}`}>{formatUzs(r.value)} UZS</span>
                      {r.count !== null && <span className={`bg-card rounded-full px-1.5 py-0.5 text-[10px] font-semibold whitespace-nowrap shadow-sm ${ZONES[r.zone].title}`}>{t('debts.people', { count: r.count })}</span>}
                    </div>
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

            {items.length === 0 ? (
              <Empty icon={<CircleCheck className="h-6 w-6" />} title={t('debts.empty')} />
            ) : (
              <div className="mt-4 space-y-5">
                {groups.map(({ zone, debts: list }) => {
                  const z = ZONES[zone]
                  return (
                    <section key={zone}>
                      <div className="mb-2.5 flex items-center justify-between gap-3 px-1">
                        <h2 className={`flex min-w-0 items-center gap-1.5 text-[11px] font-bold tracking-wider uppercase ${z.title}`}>
                          <span className={`h-2 w-2 shrink-0 rounded-full ${z.dot}`} />
                          <span className="truncate">{t(`debts.section.${zone}`)}</span>
                        </h2>
                        {counts[zone] !== null && <span className={`shrink-0 text-[11px] font-semibold ${z.title}`}>{t('debts.people', { count: counts[zone] })}</span>}
                      </div>
                      <div className="card-grid">
                        {list.map((d) => {
                          const tel = telLink(d.customerPhone)
                          return (
                            <article key={d.debtId} className={`bg-card shadow-soft rounded-card relative overflow-hidden border p-3.5 pl-4 ${z.card}`}>
                              <div className={`absolute inset-y-0 left-0 w-1 ${z.stripe}`} />
                              <Link to={`/customers/${d.customerId}`} className="flex items-start justify-between gap-3">
                                <div className="min-w-0 space-y-1.5">
                                  <p className="truncate text-sm leading-tight font-semibold">
                                    {d.customerName}
                                    {d.customerTag && <span className="text-muted text-[11px] font-normal"> ({d.customerTag})</span>}
                                  </p>
                                  <DueBadge daysLeft={d.daysLeft} dueDate={d.dueDate} />
                                </div>
                                <div className="shrink-0 text-right">
                                  <p className="tabular text-sm font-extrabold">{formatUzs(d.remaining)}</p>
                                  <p className="text-muted text-[10px] font-medium">UZS</p>
                                </div>
                              </Link>
                              <div className="border-line mt-3 flex justify-end gap-2 border-t pt-2.5">
                                {tel && (
                                  <a href={tel} aria-label={t('customer.call')} className="bg-field text-text border-line flex h-8 w-8 items-center justify-center rounded-xl border transition active:scale-95">
                                    <Phone className="h-3.5 w-3.5" />
                                  </a>
                                )}
                                <button type="button" onClick={() => remind(d)} className={`flex h-8 min-w-0 items-center gap-1.5 rounded-xl border px-3 text-xs font-semibold transition active:scale-95 ${z.action}`}>
                                  <BellRing className="h-3.5 w-3.5 shrink-0" />
                                  <span className="truncate">{t('debts.remind')}</span>
                                </button>
                              </div>
                            </article>
                          )
                        })}
                      </div>
                    </section>
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
