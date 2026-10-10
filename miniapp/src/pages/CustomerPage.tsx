import { useState } from 'react'
import { Bell, FileSpreadsheet, Minus, Pencil, Phone, Plus, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router-dom'
import { useCustomer, useDeleteTransaction, useLedger, useShop } from '../api/queries'
import type { CustomerOpenDebt, Transaction, TxType } from '../api/types'
import { useSession } from '../auth/Session'
import { Avatar } from '../components/Avatar'
import { Button } from '../components/Button'
import { SegmentedTabs } from '../components/Chips'
import { CustomerFormSheet } from '../components/CustomerFormSheet'
import { DueBadge } from '../components/DueBadge'
import { EntrySheet } from '../components/EntrySheet'
import { LedgerItem } from '../components/LedgerItem'
import { PageHeader } from '../components/ShopHeader'
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
import { confirmDialog, shareToChat } from '../lib/telegram'

export default function CustomerPage() {
  const { id = '' } = useParams()
  const { t } = useTranslation()
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

  const transactions = ledger.data?.pages.flatMap((p) => p.items) ?? []
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

  return (
    <div className="pb-28">
      <PageHeader
        title={t('customer.profile')}
        right={
          <div className="flex gap-2">
            <button type="button" onClick={() => void exportLedger()} disabled={exporting} aria-label={t('customer.export')} className="bg-card border-line flex h-10 w-10 items-center justify-center rounded-full border disabled:opacity-50">
              <FileSpreadsheet className="h-4 w-4" />
            </button>
            <button type="button" onClick={() => setEditing(true)} aria-label={t('customer.edit')} className="bg-card border-line flex h-10 w-10 items-center justify-center rounded-full border">
              <Pencil className="h-4 w-4" />
            </button>
          </div>
        }
      />

      <div className="space-y-3 px-4">
        <section className="bg-card shadow-soft rounded-card flex items-center gap-3 p-4">
          <Avatar name={c.name} seed={c.id} size="lg" />
          <div className="min-w-0 flex-1">
            <p className="truncate text-base font-extrabold">{c.name}</p>
            <p className="text-muted truncate text-xs">{[c.tag, c.phone].filter(Boolean).join(' · ') || t('customer.noContacts')}</p>
            {c.isArchived && <span className="bg-field text-muted mt-1 inline-block rounded-lg px-2 py-0.5 text-[11px] font-bold">{t('customers.filter.archived')}</span>}
          </div>
          {tel && (
            <a href={tel} aria-label={t('customer.call')} className="bg-primary-soft text-primary flex h-11 w-11 shrink-0 items-center justify-center rounded-full">
              <Phone className="h-5 w-5" />
            </a>
          )}
        </section>

        <section className="bg-dark-card rounded-card shadow-card p-5 text-white">
          <div className="flex items-start justify-between gap-2">
            <p className="text-[11px] font-bold tracking-widest text-white/60 uppercase">{t('customer.balance')}</p>
            {overdueDays > 0 && (
              <span className="inline-flex items-center gap-1 rounded-lg bg-red-500/20 px-2 py-1 text-[11px] font-bold text-red-300">
                <TriangleAlert className="h-3 w-3" />
                {t('due.overdue', { days: overdueDays })}
              </span>
            )}
          </div>
          <p className={`tabular mt-1 text-3xl font-extrabold ${c.balance < 0 ? 'text-emerald-300' : ''}`}>
            {formatUzs(c.balance)} <span className="text-sm font-bold text-white/60">UZS</span>
          </p>
          {c.balance < 0 && <p className="mt-1 text-xs text-emerald-300">{t('customer.credit')}</p>}
          <div className="mt-4 grid grid-cols-2 gap-2">
            <div className="rounded-xl bg-white/8 px-3 py-2">
              <p className="text-[10px] text-white/50">{t('customer.lastDebt')}</p>
              <p className="tabular text-xs font-bold">{c.lastDebt ? formatDateTime(c.lastDebt.createdAt, timeZone).slice(0, 10) : '—'}</p>
            </div>
            <div className="rounded-xl bg-white/8 px-3 py-2">
              <p className="text-[10px] text-white/50">{t('customer.lastPayment')}</p>
              <p className="tabular text-xs font-bold text-emerald-300">{c.lastPayment ? formatDateTime(c.lastPayment.createdAt, timeZone).slice(0, 10) : '—'}</p>
            </div>
          </div>
        </section>

        {c.notes && <p className="bg-card shadow-soft text-muted rounded-card px-4 py-3 text-xs">{c.notes}</p>}

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
                  {c.openDebts.length > 0 && <span className="bg-primary rounded-full px-1.5 text-[10px] text-white">{c.openDebts.length}</span>}
                </>
              ),
            },
          ]}
        />
      </div>

      <div className="px-4">
        {tab === 'ledger' ? (
          <>
            <SectionTitle>{t('customer.allOperations')}</SectionTitle>
            {ledger.isPending ? (
              <Loading />
            ) : transactions.length === 0 ? (
              <Empty title={t('ledger.empty')} hint={t('ledger.emptyHint')} />
            ) : (
              <div className="space-y-2.5">
                {transactions.map((tx) => (
                  <LedgerItem key={tx.id} tx={tx} timeZone={timeZone} onDelete={onDelete} />
                ))}
                <div ref={sentinel} />
                {ledger.isFetchingNextPage && <Loading />}
              </div>
            )}
          </>
        ) : (
          <>
            <SectionTitle>{t('customer.tab.open')}</SectionTitle>
            {c.openDebts.length === 0 ? (
              <Empty title={t('debts.noneForCustomer')} />
            ) : (
              <div className="space-y-2.5">
                {c.openDebts.map((d) => (
                  <article key={d.debtId} className="bg-card shadow-soft rounded-card p-3.5">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <DueBadge daysLeft={d.daysLeft} dueDate={d.dueDate} />
                        <p className="text-muted mt-1.5 text-xs">
                          {formatDate(d.dueDate)} · {d.createdByName}
                        </p>
                        {d.comment && <p className="text-muted mt-0.5 truncate text-xs italic">«{d.comment}»</p>}
                      </div>
                      <div className="shrink-0 text-right">
                        <p className="tabular text-sm font-extrabold">{formatUzs(d.remaining)}</p>
                        {d.remaining !== d.amount && <p className="text-muted tabular text-[10px]">{t('debts.of', { amount: formatUzs(d.amount) })}</p>}
                      </div>
                    </div>
                    <div className="mt-3 flex gap-2">
                      <Button size="sm" variant="soft" className="flex-1" icon={<Bell className="h-3.5 w-3.5" />} onClick={() => remind(d)}>
                        {t('debts.remind')}
                      </Button>
                      <Button size="sm" variant="success" className="flex-1" icon={<Minus className="h-3.5 w-3.5" />} onClick={() => setEntry({ type: 'payment', targetDebtId: d.debtId })}>
                        {t('customer.takePayment')}
                      </Button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </>
        )}
      </div>

      <div className="bg-bg/95 border-line fixed inset-x-0 bottom-0 z-30 border-t backdrop-blur" style={{ paddingBottom: 'calc(12px + var(--safe-bottom))' }}>
        <div className="mx-auto flex max-w-lg gap-3 px-4 pt-3">
          <Button className="flex-1" icon={<Plus className="h-4 w-4" />} onClick={() => setEntry({ type: 'debt' })}>
            {t('customer.addDebt')}
          </Button>
          <Button className="flex-1" variant="success" icon={<Minus className="h-4 w-4" />} onClick={() => setEntry({ type: 'payment' })}>
            {t('customer.takePayment')}
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
