import { useState } from 'react'
import { Bell, ChevronLeft, FileSpreadsheet, Minus, Pencil, Phone, Plus, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'
import { useCustomer, useDeleteTransaction, useLedger, useShop } from '../api/queries'
import type { CustomerOpenDebt, Transaction, TxType } from '../api/types'
import { useSession } from '../auth/Session'
import { Button, IconButton } from '../components/Button'
import { FilterChips, SegmentedTabs } from '../components/Chips'
import { CustomerFormSheet } from '../components/CustomerFormSheet'
import { DueBadge } from '../components/DueBadge'
import { EntrySheet } from '../components/EntrySheet'
import { LedgerItem } from '../components/LedgerItem'
import { Empty, ErrorState, Loading, SectionTitle } from '../components/States'
import { useBackButton } from '../hooks/useBackButton'
import { useInfiniteScroll } from '../hooks/useInfiniteScroll'
import { useToast } from '../hooks/useToast'
import { useToday } from '../hooks/useToday'
import { daysBetween, formatDate, formatDateTime } from '../lib/dates'
import { formatUzs } from '../lib/formatUzs'
import { telLink } from '../lib/people'
import { exportFile } from '../lib/export'
import { reminderText } from '../lib/reminder'
import { confirmDialog, inTelegram, shareToChat } from '../lib/telegram'

export default function CustomerPage() {
  const { id = '' } = useParams()
  const { t } = useTranslation()
  const navigate = useNavigate()
  const toast = useToast()
  const today = useToday()
  const { shop: membership } = useSession()
  useBackButton(true)

  const shop = useShop()
  const customer = useCustomer(id)
  const ledger = useLedger(id)
  const remove = useDeleteTransaction()
  const [tab, setTab] = useState<'ledger' | 'open'>('ledger')
  const [entry, setEntry] = useState<{ type: TxType; targetDebtId?: string } | null>(null)
  const [editing, setEditing] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [kind, setKind] = useState<'all' | TxType>('all')

  const loaded = ledger.data?.pages.flatMap((p) => p.items) ?? []
  // The filter works over what is loaded; the scroll sentinel keeps fetching pages until the list fills.
  const transactions = kind === 'all' ? loaded : loaded.filter((tx) => tx.type === kind)
  const sentinel = useInfiniteScroll<HTMLDivElement>(Boolean(ledger.hasNextPage) && !ledger.isFetchingNextPage, () => void ledger.fetchNextPage())

  if (customer.isPending || shop.isPending) return <Loading full />
  if (customer.isError || shop.isError) return <ErrorState onRetry={() => void customer.refetch()} message={t('errors.customer.not_found')} />

  const c = customer.data
  const timeZone = shop.data.timezone
  const tel = telLink(c.phone)
  const overdueDays = c.overdueAmount > 0 && c.nextDueDate ? daysBetween(c.nextDueDate, today) : 0

  const onDelete = async (tx: Transaction) => {
    if (!(await confirmDialog(t('ledger.deleteConfirm', { amount: formatUzs(tx.amount) })))) return
    remove.mutate(tx.id, { onSuccess: () => toast.show(t('ledger.deleted'), 'success'), onError: (e) => toast.error(e) })
  }

  const exportLedger = async () => {
    setExporting(true)
    try {
      const how = await exportFile(`/api/export/customers/${id}`)
      toast.show(t(how === 'sent' ? 'reports.fileSent' : 'reports.downloaded'), 'success')
    } catch (e) {
      toast.error(e)
    } finally {
      setExporting(false)
    }
  }

  const remind = (debt: CustomerOpenDebt) =>
    shareToChat(
      reminderText({
        language: shop.data.language,
        customerName: c.name,
        shopName: membership?.shopName ?? shop.data.name,
        amount: debt.remaining,
        dueDate: debt.dueDate,
        daysLeft: debt.daysLeft,
      })
    )

  const day = (iso: string) => formatDateTime(iso, timeZone).slice(0, 10)

  return (
    <div style={{ paddingBottom: 'calc(96px + var(--safe-bottom))' }}>
      <header className="bg-bg/90 border-line/70 sticky top-0 z-30 border-b backdrop-blur-md">
        <div className="app-x flex min-h-14 items-center gap-2 py-2">
          {!inTelegram() && (
            <button type="button" onClick={() => navigate(-1)} aria-label="Back" className="text-accent dark:border-line dark:bg-card -ml-1 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-transparent transition active:scale-95">
              <ChevronLeft className="h-5 w-5" strokeWidth={2.5} />
            </button>
          )}
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-1.5">
              <h1 className="truncate text-sm leading-tight font-bold md:text-base">
                {c.name}
                {c.tag && <span className="text-muted font-medium"> ({c.tag})</span>}
              </h1>
              {c.isArchived && <span className="bg-field text-muted border-line shrink-0 rounded border px-1.5 py-0.5 text-[10px] font-bold">{t('customers.filter.archived')}</span>}
            </div>
            <p className="text-muted tabular truncate text-xs font-medium">{c.phone || t('customer.noContacts')}</p>
          </div>
          <div className="flex shrink-0 items-center gap-1.5">
            {tel && (
              <a href={tel} aria-label={t('customer.call')} className="bg-primary-tint text-accent border-primary-line flex h-9 w-9 items-center justify-center rounded-xl border transition active:scale-95">
                <Phone className="h-4 w-4" fill="currentColor" strokeWidth={0} />
              </a>
            )}
            <IconButton onClick={() => void exportLedger()} disabled={exporting} aria-label={t('customer.export')}>
              <FileSpreadsheet className="h-4 w-4" />
            </IconButton>
            <IconButton onClick={() => setEditing(true)} aria-label={t('customer.edit')}>
              <Pencil className="h-4 w-4" />
            </IconButton>
          </div>
        </div>
      </header>

      <div className="app-x space-y-4 pt-4">
        <section className="hero-gradient relative overflow-hidden rounded-2xl p-5 text-white">
          <div className="pointer-events-none absolute -right-8 -bottom-8 h-36 w-36 rounded-full bg-sky-300/25 blur-2xl" />
          <div className="pointer-events-none absolute -top-10 -left-10 h-32 w-32 rounded-full bg-blue-700/40 blur-xl" />
          <div className="relative">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-xs font-semibold tracking-wider text-white/80 uppercase">{t('customer.balance')}</p>
              {overdueDays > 0 && (
                <span className="inline-flex items-center gap-1.5 rounded-full border border-white/20 bg-rose-500/30 px-3 py-1 text-xs font-semibold whitespace-nowrap backdrop-blur dark:border-rose-500/35 dark:bg-rose-500/15 dark:text-rose-300">
                  <TriangleAlert className="h-3.5 w-3.5 shrink-0" />
                  {t('due.overdue', { days: overdueDays })}
                </span>
              )}
            </div>
            <p className={`tabular mt-2 flex flex-wrap items-baseline gap-x-2 text-3xl font-extrabold tracking-tight md:text-4xl ${c.balance < 0 ? 'text-emerald-200' : ''}`}>
              {formatUzs(c.balance)} <span className="text-lg font-semibold text-white/75">UZS</span>
            </p>
            {c.balance < 0 && <p className="mt-1 text-xs text-emerald-200">{t('customer.credit')}</p>}
            <div className="mt-4 grid grid-cols-2 gap-2">
              <div className="hero-chip min-w-0 rounded-xl p-3">
                <p className="truncate text-[10px] font-bold tracking-wider text-white/75 uppercase">{t('customer.lastDebt')}</p>
                <p className="tabular mt-1 truncate text-xs font-semibold">{c.lastDebt ? day(c.lastDebt.createdAt) : '—'}</p>
              </div>
              <div className="hero-chip min-w-0 rounded-xl p-3">
                <p className="truncate text-[10px] font-bold tracking-wider text-white/75 uppercase dark:text-emerald-300/80">{t('customer.lastPayment')}</p>
                <p className="tabular mt-1 truncate text-xs font-semibold text-emerald-200 dark:text-emerald-300">{c.lastPayment ? day(c.lastPayment.createdAt) : '—'}</p>
              </div>
            </div>
          </div>
        </section>

        {c.notes && <p className="bg-card border-line text-muted rounded-card border px-4 py-3 text-xs leading-relaxed">{c.notes}</p>}

        <SegmentedTabs<'ledger' | 'open'>
          value={tab}
          onChange={setTab}
          options={[
            { value: 'ledger', label: t('customer.tab.ledger') },
            {
              value: 'open',
              label: (
                <>
                  {t('customer.tab.open')}
                  {c.openDebts.length > 0 && <span className="bg-danger-fill dark:bg-danger-soft dark:text-danger dark:border-danger-line rounded-full border border-transparent px-1.5 py-0.5 text-[10px] leading-none font-bold text-white">{c.openDebts.length}</span>}
                </>
              ),
            },
          ]}
        />

        {tab === 'ledger' ? (
          <section>
            <FilterChips<'all' | TxType>
              shape="tag"
              value={kind}
              onChange={setKind}
              options={[
                { value: 'all', label: t('customers.filter.all') },
                { value: 'debt', label: t('ledger.onlyDebts') },
                { value: 'payment', label: t('ledger.onlyPayments') },
              ]}
            />
            <SectionTitle right={!ledger.hasNextPage && transactions.length > 0 ? t('ledger.count', { count: transactions.length }) : undefined}>{t('customer.allOperations')}</SectionTitle>
            {ledger.isPending ? (
              <Loading />
            ) : loaded.length === 0 ? (
              <Empty title={t('ledger.empty')} hint={t('ledger.emptyHint')} />
            ) : (
              <>
                <div className="space-y-3">
                  {transactions.map((tx) => (
                    <LedgerItem key={tx.id} tx={tx} timeZone={timeZone} onDelete={onDelete} />
                  ))}
                </div>
                {transactions.length === 0 && !ledger.hasNextPage && <Empty title={t('ledger.nothing')} />}
                <div ref={sentinel} />
                {ledger.isFetchingNextPage && <Loading />}
              </>
            )}
          </section>
        ) : (
          <section>
            <SectionTitle>{t('customer.tab.open')}</SectionTitle>
            {c.openDebts.length === 0 ? (
              <Empty title={t('debts.noneForCustomer')} />
            ) : (
              <div className="card-grid">
                {c.openDebts.map((d) => (
                  <article key={d.debtId} className="bg-card border-line shadow-soft rounded-card border p-3.5">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <DueBadge daysLeft={d.daysLeft} dueDate={d.dueDate} />
                        <p className="text-muted mt-1.5 truncate text-xs">
                          {formatDate(d.dueDate)} · {d.createdByName}
                        </p>
                        {d.comment && <p className="text-muted mt-0.5 truncate text-xs">{d.comment}</p>}
                      </div>
                      <div className="shrink-0 text-right">
                        <p className="tabular text-sm font-extrabold">{formatUzs(d.remaining)}</p>
                        {d.remaining !== d.amount ? <p className="text-muted tabular text-[10px]">{t('debts.of', { amount: formatUzs(d.amount) })}</p> : <p className="text-muted text-[10px] font-semibold">UZS</p>}
                      </div>
                    </div>
                    <div className="border-line mt-3 flex gap-2 border-t pt-3">
                      <Button size="sm" variant="soft" className="flex-1" icon={<Bell className="h-3.5 w-3.5 shrink-0" />} onClick={() => remind(d)}>
                        <span className="truncate">{t('debts.remind')}</span>
                      </Button>
                      <Button size="sm" variant="success" className="flex-1" icon={<Minus className="h-3.5 w-3.5 shrink-0" />} onClick={() => setEntry({ type: 'payment', targetDebtId: d.debtId })}>
                        <span className="truncate">{t('customer.takePayment')}</span>
                      </Button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>
        )}
      </div>

      <div className="bg-bg/95 border-line shadow-bar fixed inset-x-0 bottom-0 z-30 border-t backdrop-blur-xl" style={{ paddingBottom: 'calc(12px + var(--safe-bottom))' }}>
        <div className="app-frame app-x flex gap-3 pt-3">
          <Button className="flex-1 px-2" icon={<Plus className="h-5 w-5 shrink-0" />} onClick={() => setEntry({ type: 'debt' })}>
            <span className="truncate">{t('customer.addDebt')}</span>
          </Button>
          <Button className="flex-1 px-2" variant="success" icon={<Minus className="h-5 w-5 shrink-0" />} onClick={() => setEntry({ type: 'payment' })}>
            <span className="truncate">{t('customer.takePayment')}</span>
          </Button>
        </div>
      </div>

      {entry && (
        <EntrySheet
          onClose={() => setEntry(null)}
          customer={c}
          initialType={entry.type}
          initialTargetDebtId={entry.targetDebtId}
          today={today}
          defaultDueDays={shop.data.defaultDueDays}
        />
      )}
      {editing && <CustomerFormSheet onClose={() => setEditing(false)} customer={c} />}
    </div>
  )
}
