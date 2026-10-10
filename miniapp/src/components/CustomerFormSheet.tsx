import { useState } from 'react'
import { Archive, ArchiveRestore } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useCreateCustomer, useUpdateCustomer } from '../api/queries'
import type { CustomerDetail } from '../api/types'
import { useToast } from '../hooks/useToast'
import { BottomSheet } from './BottomSheet'
import { Button } from './Button'
import { Field, inputClass } from './States'

interface Props {
  onClose(): void
  /** Existing customer to edit; omitted = create. */
  customer?: CustomerDetail
  initialName?: string
  onSaved?(customer: CustomerDetail): void
}

/** Render only while visible: the fields start from the props on every mount. */
export function CustomerFormSheet({ onClose, customer, initialName, onSaved }: Props) {
  const { t } = useTranslation()
  const toast = useToast()
  const create = useCreateCustomer()
  const update = useUpdateCustomer(customer?.id ?? '')
  const [name, setName] = useState(customer?.name ?? initialName ?? '')
  const [tag, setTag] = useState(customer?.tag ?? '')
  const [phone, setPhone] = useState(customer?.phone ?? '')
  const [notes, setNotes] = useState(customer?.notes ?? '')

  const pending = create.isPending || update.isPending
  const done = (saved: CustomerDetail) => {
    toast.show(t('common.saved'), 'success')
    onSaved?.(saved)
    onClose()
  }

  const save = () => {
    if (!name.trim()) {
      toast.show(t('customer.needName'), 'error')
      return
    }
    const input = { name: name.trim(), tag: tag.trim(), phone: phone.trim(), notes: notes.trim() }
    const options = { onSuccess: done, onError: (e: unknown) => toast.error(e) }
    if (customer) update.mutate(input, options)
    else create.mutate(input, options)
  }

  return (
    <BottomSheet
      open
      onClose={onClose}
      title={t(customer ? 'customer.edit' : 'customer.new')}
      footer={
        <Button full size="lg" loading={pending} onClick={save}>
          {t('common.save')}
        </Button>
      }
    >
      <Field label={t('customer.name')}>
        <input value={name} maxLength={128} autoFocus={!customer} onChange={(e) => setName(e.target.value)} placeholder="Ahmad aka" className={inputClass} />
      </Field>
      <Field label={t('customer.tag')} hint={t('customer.tagHint')}>
        <input value={tag} maxLength={64} onChange={(e) => setTag(e.target.value)} placeholder="stomatolog" className={inputClass} />
      </Field>
      <Field label={t('customer.phone')}>
        <input value={phone} maxLength={32} inputMode="tel" onChange={(e) => setPhone(e.target.value)} placeholder="+998 90 123-45-67" className={inputClass} />
      </Field>
      <Field label={t('customer.notes')}>
        <textarea value={notes} maxLength={1000} rows={2} onChange={(e) => setNotes(e.target.value)} className={`${inputClass} h-auto py-3`} />
      </Field>
      {customer && (
        <Button
          full
          variant="outline"
          icon={customer.isArchived ? <ArchiveRestore className="h-4 w-4" /> : <Archive className="h-4 w-4" />}
          loading={update.isPending}
          onClick={() => update.mutate({ isArchived: !customer.isArchived }, { onSuccess: done, onError: (e) => toast.error(e) })}
        >
          {t(customer.isArchived ? 'customer.unarchive' : 'customer.archive')}
        </Button>
      )}
    </BottomSheet>
  )
}
