import { useState } from 'react'
import { FileSpreadsheet, Send, TrendingDown, TrendingUp } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { useReport } from '../api/queries'
import { useSession } from '../auth/Session'
import { Avatar } from '../components/Avatar'
import { Button } from '../components/Button'
import { BarChart, StatCard } from '../components/Charts'
import { FilterChips } from '../components/Chips'
import { DueBadge } from '../components/DueBadge'
import { ShopHeader } from '../components/ShopHeader'
import { Empty, ErrorState, Loading, SectionTitle } from '../components/States'
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

  return (
    <>
      <ShopHeader />
      <div className="px-4">
        <h1 className="px-1 text-xl font-extrabold">{t('reports.title')}</h1>
        <p className="text-muted px-1 text-xs">
          {formatDate(range.from)} — {formatDate(range.to)}
        </p>
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
          <>
            <section className="bg-card shadow-soft rounded-card mt-3 p-4">
              <div className="flex items-start justify-between gap-2">
                <p className="text-muted text-[11px] font-extrabold tracking-widest uppercase">{t('reports.collected')}</p>
                {change !== null && (
                  <span className={`inline-flex items-center gap-1 rounded-lg px-2 py-0.5 text-[11px] font-bold ${change >= 0 ? 'bg-success-soft text-success' : 'bg-danger-soft text-danger'}`}>
                    {change >= 0 ? <TrendingUp className="h-3 w-3" /> : <TrendingDown className="h-3 w-3" />}
                    {change > 0 ? '+' : ''}
                    {change}%
                  </span>
                )}
              </div>
              <p className="tabular mt-1 text-3xl font-extrabold">
                {formatUzs(r.collected)} <span className="text-muted text-sm font-bold">UZS</span>
              </p>
              <p className="text-muted mt-1 text-xs">{t('reports.payments', { count: r.paymentsCount })}</p>
            </section>

            <div className="mt-3 flex gap-3">
              <StatCard label={t('reports.newDebts')} value={formatUzs(r.newDebts)} unit="UZS">
                <p className="text-muted mt-1 text-[11px]">{t('reports.debts', { count: r.debtsCount })}</p>
              </StatCard>
              <StatCard label={t('reports.rate')} value={`${Math.round(r.collectionRate * 100)}%`} tone={r.collectionRate >= 0.8 ? 'success' : undefined}>
                <div className="bg-field mt-2 h-1.5 overflow-hidden rounded-full">
                  <div className="bg-success h-full rounded-full" style={{ width: `${Math.min(100, Math.round(r.collectionRate * 100))}%` }} />
                </div>
              </StatCard>
            </div>

            <div className="mt-3 flex gap-3">
              <StatCard label={t('reports.openTotal')} value={formatUzs(r.openTotal)} unit="UZS" />
              <StatCard label={t('debts.overdue')} value={formatUzs(r.overdueTotal)} unit="UZS" tone={r.overdueTotal > 0 ? 'danger' : undefined} />
            </div>

            <section className="bg-card shadow-soft rounded-card mt-3 p-4">
              <p className="text-sm font-extrabold">{t('reports.flow')}</p>
              <div className="text-muted mt-1 mb-4 flex gap-4 text-[11px] font-bold">
                <span className="flex items-center gap-1.5">
                  <span className="bg-primary-light h-2 w-2 rounded-full" />
                  {t('reports.legendDebts')}
                </span>
                <span className="flex items-center gap-1.5">
                  <span className="bg-success h-2 w-2 rounded-full" />
                  {t('reports.legendPayments')}
                </span>
              </div>
              {r.flow.every((f) => f.debts === 0 && f.payments === 0) ? (
                <p className="text-muted py-6 text-center text-xs">{t('reports.noFlow')}</p>
              ) : (
                <BarChart colorA="#8b5cf6" colorB="#10b981" bars={r.flow.map((f) => ({ label: formatDayMonth(f.from), a: f.debts, b: f.payments }))} />
              )}
            </section>

            <SectionTitle>{t('reports.topDebtors')}</SectionTitle>
            {r.topDebtors.length === 0 ? (
              <Empty title={t('debts.empty')} />
            ) : (
              <div className="bg-card shadow-soft rounded-card overflow-hidden">
                {r.topDebtors.map((d) => (
                  <Link key={d.customerId} to={`/customers/${d.customerId}`} className="border-line flex items-center gap-3 border-b px-4 py-3 last:border-b-0">
                    <Avatar name={d.name} seed={d.customerId} size="sm" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-bold">
                        {d.name}
                        {d.tag && <span className="text-muted font-semibold"> ({d.tag})</span>}
                      </p>
                      {d.nextDueDate && (
                        <div className="mt-1">
                          <DueBadge daysLeft={daysBetween(today, d.nextDueDate)} dueDate={d.nextDueDate} />
                        </div>
                      )}
                    </div>
                    <div className="shrink-0 text-right">
                      <p className={`tabular text-sm font-extrabold ${d.overdue > 0 ? 'text-danger' : ''}`}>{formatUzs(d.balance)}</p>
                      <p className="text-muted text-[10px] font-bold">UZS</p>
                    </div>
                  </Link>
                ))}
              </div>
            )}

            <div className="mt-4 flex gap-3 pb-2">
              {isOwner && (
                <Button full variant="outline" icon={<FileSpreadsheet className="h-4 w-4" />} loading={busy === 'excel'} disabled={busy !== null} onClick={() => void run('excel')}>
                  {t('reports.excel')}
                </Button>
              )}
              <Button full variant="soft" icon={<Send className="h-4 w-4" />} loading={busy === 'telegram'} disabled={busy !== null} onClick={() => void run('telegram')}>
                {t('reports.sendTelegram')}
              </Button>
            </div>
          </>
        )}
      </div>
    </>
  )
}
