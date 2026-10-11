import { useState } from 'react'
import { Check, Mic, Plus, Square } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { parseEntryAudio, useCreateTransaction } from '../api/queries'
import type { CustomerDetail, TxType } from '../api/types'
import { recorderSupported, useRecorder } from '../hooks/useRecorder'
import { useToast } from '../hooks/useToast'
import { addDays, formatDate } from '../lib/dates'
import { formatUzs } from '../lib/formatUzs'
import { displayName } from '../lib/people'
import { AmountInput } from './AmountInput'
import { BottomSheet } from './BottomSheet'
import { Button } from './Button'
import { SegmentedTabs } from './Chips'
import { DuePicker } from './DuePicker'
import { inputClass } from './States'

interface Props {
  onClose(): void
  customer: CustomerDetail
  initialType: TxType
  /** Pre-selected debt when "take payment" was pressed on a specific open debt. */
  initialTargetDebtId?: string
  today: string
  defaultDueDays: number
}

/**
 * "New operation" sheet: debt or payment for one customer, with quick amounts, a term and optional voice input.
 * Render it only while it should be visible: its state starts from the props on every mount.
 */
export function EntrySheet({ onClose, customer, initialType, initialTargetDebtId, today, defaultDueDays }: Props) {
  const { t } = useTranslation()
  const toast = useToast()
  const create = useCreateTransaction()
  const [type, setType] = useState<TxType>(initialType)
  const [amount, setAmount] = useState(0)
  const [due, setDue] = useState(addDays(today, defaultDueDays))
  const [comment, setComment] = useState('')
  const [targetDebtId, setTargetDebtId] = useState(initialTargetDebtId ?? '')
  const [parsing, setParsing] = useState(false)

  const voice = useRecorder(
    30,
    async ({ blob, fileName }) => {
      setParsing(true)
      try {
        const parsed = await parseEntryAudio(blob, fileName)
        if (parsed.amount) setAmount(parsed.amount)
        setType(parsed.type)
        if (parsed.type === 'debt' && parsed.dueDate && !parsed.dueDateIsDefault) setDue(parsed.dueDate)
        setComment(parsed.comment ?? parsed.transcript)
        toast.show(`🗣 ${parsed.transcript}`)
      } catch (e) {
        toast.error(e)
      } finally {
        setParsing(false)
      }
    },
    () => toast.show(t('voice.denied'), 'error')
  )

  const target = customer.openDebts.find((d) => d.debtId === targetDebtId)
  const fullAmount = target ? target.remaining : Math.max(customer.balance, 0)

  const submit = () => {
    if (amount <= 0) {
      toast.show(t('entry.needAmount'), 'error')
      return
    }
    create.mutate(
      {
        customerId: customer.id,
        type,
        amount,
        comment: comment.trim() || undefined,
        dueDate: type === 'debt' ? due : undefined,
        targetDebtId: type === 'payment' && targetDebtId ? targetDebtId : undefined,
      },
      {
        onSuccess: () => {
          toast.show(t(type === 'debt' ? 'entry.savedDebt' : 'entry.savedPayment'), 'success')
          onClose()
        },
        onError: (e) => toast.error(e),
      }
    )
  }

  return (
    <BottomSheet
      open
      onClose={onClose}
      title={
        <span className="bg-primary-tint border-primary-line inline-flex max-w-full items-center gap-2 rounded-full border py-1 pr-3 pl-1">
          <span className="bg-primary flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-[11px] font-bold text-white">{[...customer.name.trim()][0]?.toUpperCase() ?? '?'}</span>
          <span className="truncate text-xs font-bold tracking-tight">{displayName(customer.name, customer.tag)}</span>
          {customer.phone && <span className="text-muted tabular hidden shrink-0 text-[11px] font-medium sm:inline">• {customer.phone}</span>}
        </span>
      }
      footer={
        <>
          <Button full size="lg" variant={type === 'debt' ? 'primary' : 'success'} loading={create.isPending} onClick={submit} className="flex-wrap font-extrabold">
            <span className="truncate">{t('entry.submit')}</span>
            {amount > 0 && (
              <span className="tabular rounded-full bg-white/20 px-2 py-0.5 text-xs font-semibold whitespace-nowrap">
                {formatUzs(amount)} UZS · {t(type === 'debt' ? 'entry.debtShort' : 'entry.paymentShort')}
              </span>
            )}
          </Button>
          <button type="button" onClick={onClose} className="text-muted mt-1.5 h-9 w-full text-sm font-semibold">
            {t('common.cancel')}
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <SegmentedTabs<TxType>
          tone="primary"
          value={type}
          onChange={setType}
          options={[
            { value: 'debt', label: (<><Plus className="h-4 w-4 shrink-0" />{t('entry.typeDebt')}</>) },
            { value: 'payment', activeClass: 'bg-success-fill text-white shadow-soft', label: (<><Check className={`h-4 w-4 shrink-0 ${type === 'payment' ? '' : 'text-success'}`} />{t('entry.typePayment')}</>) },
          ]}
        />

        <AmountInput
          value={amount}
          onChange={setAmount}
          label={t('entry.amount')}
          full={type === 'payment' ? { label: t('entry.full'), value: fullAmount } : undefined}
        />

        {type === 'debt' ? (
          <DuePicker today={today} value={due} onChange={setDue} />
        ) : (
          customer.openDebts.length > 1 && (
            <label className="block">
              <span className="text-muted mb-1.5 block text-[11px] font-bold tracking-wider uppercase">{t('entry.targetDebt')}</span>
              <select value={targetDebtId} onChange={(e) => setTargetDebtId(e.target.value)} className={inputClass}>
                <option value="">{t('entry.targetOldest')}</option>
                {customer.openDebts.map((d) => (
                  <option key={d.debtId} value={d.debtId}>
                    {formatUzs(d.remaining)} UZS · {formatDate(d.dueDate)}
                  </option>
                ))}
              </select>
            </label>
          )
        )}

        <div>
          <span className="text-muted mb-1.5 block text-[11px] font-bold tracking-wider uppercase">{t('entry.comment')}</span>
          <div className="bg-field border-line focus-within:border-accent focus-within:ring-accent/25 flex items-center gap-2 rounded-xl border py-1.5 pr-1.5 pl-3.5 transition focus-within:ring-2">
            <input
              value={comment}
              maxLength={500}
              onChange={(e) => setComment(e.target.value)}
              placeholder={t('entry.commentPlaceholder')}
              className="placeholder:text-muted h-9 min-w-0 flex-1 bg-transparent text-base outline-none md:text-sm"
            />
            {recorderSupported() && (
              <Button
                size="sm"
                variant={voice.recording ? 'danger' : 'soft'}
                loading={parsing}
                icon={voice.recording ? <Square className="h-3.5 w-3.5" /> : <Mic className="h-3.5 w-3.5" />}
                onClick={voice.recording ? voice.stop : voice.start}
                className="shrink-0"
              >
                {t(voice.recording ? 'voice.stop' : 'voice.record')}
              </Button>
            )}
          </div>
          {!recorderSupported() && <p className="text-muted mt-1.5 text-[11px]">{t('voice.unsupported')}</p>}
        </div>
      </div>
    </BottomSheet>
  )
}
