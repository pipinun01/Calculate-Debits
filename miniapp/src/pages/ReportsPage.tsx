import { useState } from 'react'
import { ChartNoAxesCombined, Clock, FileSpreadsheet, Send, TrendingDown, TrendingUp } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { useReport } from '../api/queries'
import { useSession } from '../auth/Session'
import { Avatar } from '../components/Avatar'
import { Button } from '../components/Button'
import { BarChart, ProgressBar, StatCard } from '../components/Charts'
import { FilterChips } from '../components/Chips'
import { DueBadge } from '../components/DueBadge'
import { PageTitle, ShopHeader } from '../components/ShopHeader'
import { Empty, ErrorState, Loading } from '../components/States'
import { useToast } from '../hooks/useToast'
import { useToday } from '../hooks/useToday'
import { daysBetween, formatDate, formatDayMonth, periodRange, type ReportPeriod } from '../lib/dates'
import { exportFile } from '../lib/export'
import { formatUzs } from '../lib/formatUzs'

/** Change against the previous period in percent; null when there is nothing to compare with. */
function delta(current: number, previous: number): number | null {
  if (previous === 0) return null
  return Math.round(((current - previous) / previous) * 100)
}

export default function ReportsPage() {
  const { t } = useTranslation()
  const today = useToday()
  const [period, setPeriod] = useState<ReportPeriod>('month')
  const range = periodRange(period, today)
  const report = useReport(range.from, range.to)
  const r = report.data
  const change = r ? delta(r.collected, r.collectedPrevious) : null
  const toast = useToast()
  const { isOwner } = useSession()
  const [busy, setBusy] = useState<'excel' | 'telegram' | null>(null)

  const run = async (kind: 'excel' | 'telegram') => {
    setBusy(kind)
    try {
      if (kind === 'telegram') {
        await api<void>('POST', '/api/reports/send', range)
        toast.show(t('reports.summarySent'), 'success')
      } else {
        const how = await exportFile('/api/export/shop', range)
        toast.show(t(how === 'sent' ? 'reports.fileSent' : 'reports.downloaded'), 'success')
      }
    } catch (e) {
      toast.error(e)
    } finally {
      setBusy(null)
    }
  }

  // "Payments are 2.1× the new debts" — only when both sides have something to compare.
  const ratio = r && r.collected > 0 && r.newDebts > 0 ? Math.max(r.collected, r.newDebts) / Math.min(r.collected, r.newDebts) : null
  const healthy = r ? r.collected >= r.newDebts : true
  const card = 'bg-card border-line shadow-card min-w-0 rounded-3xl border p-5'

  return (
    <>
      <ShopHeader />
      <div className="app-x">
        <PageTitle title={t('reports.title')} subtitle={`${formatDate(range.from)} — ${formatDate(range.to)}`} />
        <div className="mt-3">
          <FilterChips<ReportPeriod>
            value={period}
            onChange={setPeriod}
            options={[
              { value: 'month', label: t('reports.period.month') },
              { value: 'prevMonth', label: t('reports.period.prevMonth') },
              { value: 'quarter', label: t('reports.period.quarter') },
              { value: 'year', label: t('reports.period.year') },
            ]}
          />
        </div>

        {report.isPending ? (
          <Loading full />
        ) : report.isError || !r ? (
          <ErrorState onRetry={() => void report.refetch()} />
        ) : (
          <div className="mt-3 space-y-4">
            <section className="border-primary-line from-card to-primary-tint shadow-card relative overflow-hidden rounded-3xl border bg-linear-to-br p-5">
              <div className="pointer-events-none absolute -top-10 -right-10 h-36 w-36 rounded-full bg-sky-300/25 blur-2xl dark:bg-sky-500/10" />
              <div className="relative space-y-4">
                <div className="flex items-center justify-between gap-2">
                  <p className="text-muted min-w-0 truncate text-xs font-bold tracking-wider uppercase">{t('reports.collected')}</p>
                  {change !== null && (
                    <span className={`inline-flex shrink-0 items-center gap-1 rounded-full border px-2.5 py-1 text-xs font-bold ${change >= 0 ? 'bg-success-soft text-success border-success-line' : 'bg-danger-soft text-danger border-danger-line'}`}>
                      {change >= 0 ? <TrendingUp className="h-3.5 w-3.5" /> : <TrendingDown className="h-3.5 w-3.5" />}
                      {change > 0 ? '+' : ''}
                      {change}%
                    </span>
                  )}
                </div>
                <div>
                  <p className="tabular flex flex-wrap items-baseline gap-x-1.5 text-3xl font-extrabold tracking-tight md:text-4xl">
                    {formatUzs(r.collected)} <span className="text-muted text-sm font-bold">UZS</span>
                  </p>
                  <p className="text-muted mt-0.5 text-xs">{t('reports.payments', { count: r.paymentsCount })}</p>
                </div>
                <div className="bg-line h-px" />
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                  <StatCard label={t('reports.newDebts')} value={formatUzs(r.newDebts)} unit="UZS">
                    <ProgressBar value={r.collected + r.newDebts > 0 ? r.newDebts / (r.collected + r.newDebts) : 0} color="var(--color-chart)" />
                  </StatCard>
                  <StatCard
                    label={t('reports.rate')}
                    value={`${Math.round(r.collectionRate * 100)}%`}
                    tone={r.collectionRate >= 0.8 ? 'success' : undefined}
                    right={r.collectionRate >= 0.8 ? <span className="text-success shrink-0 text-[10px] font-bold uppercase">{t('reports.good')}</span> : undefined}
                  >
                    <ProgressBar value={r.collectionRate} color="var(--color-success-fill)" />
                  </StatCard>
                  <StatCard label={t('reports.openTotal')} value={formatUzs(r.openTotal)} unit="UZS" />
                  <StatCard label={t('debts.overdue')} value={formatUzs(r.overdueTotal)} unit="UZS" tone={r.overdueTotal > 0 ? 'danger' : undefined} />
                </div>
              </div>
            </section>

            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              <section className={`${card} space-y-4`}>
                <div>
                  <h2 className="text-base font-bold">{t('reports.flow')}</h2>
                  <p className="text-muted text-xs">{t('reports.debts', { count: r.debtsCount })}</p>
                </div>
                <div className="flex flex-wrap gap-x-4 gap-y-1">
                  <span className="flex items-center gap-1.5 text-xs font-semibold">
                    <span className="h-3 w-3 rounded-full bg-emerald-500" />
                    {t('reports.legendPayments')}
                  </span>
                  <span className="flex items-center gap-1.5 text-xs font-semibold">
                    <span className="bg-chart h-3 w-3 rounded-full" />
                    {t('reports.legendDebts')}
                  </span>
                </div>
                {r.flow.every((f) => f.debts === 0 && f.payments === 0) ? (
                  <p className="text-muted py-8 text-center text-xs">{t('reports.noFlow')}</p>
                ) : (
                  <BarChart colorA="#10b981" colorB="var(--color-chart)" bars={r.flow.map((f) => ({ label: formatDayMonth(f.from), a: f.payments, b: f.debts }))} />
                )}
                {ratio !== null && (
                  <div className={`flex items-center justify-between gap-2 rounded-2xl border p-3 ${healthy ? 'bg-primary-tint border-primary-line' : 'bg-warning-soft border-warning-line'}`}>
                    <div className="flex min-w-0 items-center gap-2.5">
                      <span className={`bg-card flex h-8 w-8 shrink-0 items-center justify-center rounded-full shadow-sm ${healthy ? 'text-accent' : 'text-warning'}`}>
                        <ChartNoAxesCombined className="h-4 w-4" />
                      </span>
                      <span className="text-xs leading-snug font-semibold">{t(healthy ? 'reports.insightUp' : 'reports.insightDown', { x: ratio.toFixed(1) })}</span>
                    </div>
                    <span className={`shrink-0 rounded-full px-2 py-0.5 text-xs font-bold ${healthy ? 'bg-primary-soft text-accent' : 'bg-card text-warning'}`}>{t(healthy ? 'reports.stable' : 'reports.attention')}</span>
                  </div>
                )}
              </section>

              <section className={`${card} space-y-3`}>
                <div className="flex items-center justify-between gap-3">
                  <h2 className="min-w-0 truncate text-base font-bold">{t('reports.topDebtors')}</h2>
                  <Link to="/debts" className="text-accent shrink-0 text-xs font-bold">
                    {t('reports.seeAll')}
                  </Link>
                </div>
                {r.topDebtors.length === 0 ? (
                  <Empty title={t('debts.empty')} />
                ) : (
                  <div className="space-y-2.5">
                    {r.topDebtors.map((d) => {
                      const late = d.overdue > 0
                      const daysLeft = d.nextDueDate ? daysBetween(today, d.nextDueDate) : null
                      return (
                        <Link key={d.customerId} to={`/customers/${d.customerId}`} className={`flex items-center justify-between gap-3 rounded-2xl border p-3 transition active:scale-[0.99] ${late ? 'bg-danger-soft border-danger-line' : 'bg-field border-line'}`}>
                          <div className="flex min-w-0 items-center gap-3">
                            <Avatar name={d.name} seed={d.customerId} size="sm" round />
                            <div className="min-w-0">
                              <p className="truncate text-sm leading-tight font-bold">
                                {d.name}
                                {d.tag && <span className="text-muted font-medium"> ({d.tag})</span>}
                              </p>
                              {late && daysLeft !== null && daysLeft < 0 && (
                                <p className="text-danger mt-0.5 flex items-center gap-1 text-xs font-semibold">
                                  <Clock className="h-3 w-3" />
                                  {t('due.overdue', { days: -daysLeft })}
                                </p>
                              )}
                            </div>
                          </div>
                          <div className="flex shrink-0 flex-col items-end gap-1">
                            <p className={`tabular text-sm font-bold ${late ? 'text-danger' : ''}`}>
                              {formatUzs(d.balance)} <span className="text-[11px] font-normal opacity-70">UZS</span>
                            </p>
                            {late ? (
                              <span className="bg-card text-danger border-danger-line rounded-full border px-2 py-0.5 text-[10px] font-semibold">{t('debts.overdue')}</span>
                            ) : (
                              d.nextDueDate && daysLeft !== null && <DueBadge daysLeft={daysLeft} dueDate={d.nextDueDate} />
                            )}
                          </div>
                        </Link>
                      )
                    })}
                  </div>
                )}
              </section>
            </div>

            <div className="grid grid-cols-1 gap-2.5 pb-2 md:grid-cols-2">
              {isOwner && (
                <Button full variant="outline" size="lg" className="text-sm font-semibold" icon={<FileSpreadsheet className="text-accent h-5 w-5 shrink-0" />} loading={busy === 'excel'} disabled={busy !== null} onClick={() => void run('excel')}>
                  <span className="truncate">{t('reports.excelFull')}</span>
                </Button>
              )}
              <Button full variant="telegram" size="lg" className="text-sm font-semibold" icon={<Send className="h-5 w-5 shrink-0" />} loading={busy === 'telegram'} disabled={busy !== null} onClick={() => void run('telegram')}>
                <span className="truncate">{t('reports.sendTelegram')}</span>
              </Button>
            </div>
          </div>
        )}
      </div>
    </>
  )
}
