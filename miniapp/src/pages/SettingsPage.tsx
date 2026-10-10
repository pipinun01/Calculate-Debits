import { useState } from 'react'
import { ArrowLeftRight, Bell, Calendar, Clock, Globe, History, LogOut, MessageSquare, Pencil, Send, Store, Users, Wallet } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useShop, useUpdateShop, type ShopInput } from '../api/queries'
import type { Language } from '../api/types'
import { useSession } from '../auth/Session'
import { BottomSheet } from '../components/BottomSheet'
import { Button } from '../components/Button'
import { SegmentedTabs } from '../components/Chips'
import { SettingGroup, SettingRow } from '../components/SettingRow'
import { ShopHeader } from '../components/ShopHeader'
import { ErrorState, Field, Loading, SectionTitle, inputClass } from '../components/States'
import { useToast } from '../hooks/useToast'
import { LANGUAGES } from '../i18n'
import { closeApp, confirmDialog, inTelegram } from '../lib/telegram'

const DUE_PRESETS = [15, 30, 45]

export default function SettingsPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const toast = useToast()
  const { me, shop: membership, isOwner, setLanguage, reload } = useSession()
  const shop = useShop()
  const update = useUpdateShop()
  const [renaming, setRenaming] = useState(false)
  const [name, setName] = useState('')
  const [customDue, setCustomDue] = useState(false)

  if (shop.isPending) return <Loading full />
  if (shop.isError) return <ErrorState onRetry={() => void shop.refetch()} />
  const s = shop.data

  const save = (input: ShopInput, done?: () => void) =>
    update.mutate(input, {
      onSuccess: () => {
        toast.show(t('common.saved'), 'success')
        done?.()
      },
      onError: (e) => toast.error(e),
    })

  const languageOptions = LANGUAGES.map((l) => ({ value: l, label: t(`lang.${l}`) }))

  const leave = async () => {
    if (!(await confirmDialog(t('settings.leaveConfirm', { shop: s.name })))) return
    try {
      await api<void>('POST', '/api/members/leave')
      const next = await reload()
      if (next.shops.length === 0 && inTelegram()) closeApp()
      else navigate('/shops')
    } catch (e) {
      toast.error(e)
    }
  }

  return (
    <>
      <ShopHeader />
      <div className="px-4">
        <h1 className="px-1 text-xl font-extrabold">{t('settings.title')}</h1>
        <p className="text-muted px-1 text-xs">{t('settings.subtitle')}</p>

        <section className="bg-card shadow-soft rounded-card mt-3 p-4">
          <div className="flex items-center gap-3">
            <div className="bg-primary-soft text-primary flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl">
              <Store className="h-6 w-6" />
            </div>
            <div className="min-w-0 flex-1">
              <p className="truncate text-base font-extrabold">{s.name}</p>
              <p className="text-muted truncate text-xs">
                {me.firstName} · {t(`role.${membership?.role ?? 'worker'}`)}
              </p>
            </div>
            {isOwner && (
              <button
                type="button"
                aria-label={t('settings.rename')}
                onClick={() => {
                  setName(s.name)
                  setRenaming(true)
                }}
                className="bg-field text-muted flex h-9 w-9 items-center justify-center rounded-full"
              >
                <Pencil className="h-4 w-4" />
              </button>
            )}
          </div>
          <Button full variant="soft" className="mt-3" icon={<ArrowLeftRight className="h-4 w-4" />} onClick={() => navigate('/shops')}>
            {t('settings.switchShop')}
          </Button>
        </section>

        <SectionTitle right={isOwner ? undefined : t('settings.ownerOnly')}>{t('settings.shop')}</SectionTitle>
        <SettingGroup>
          <SettingRow icon={<Calendar className="h-4 w-4" />} title={t('settings.dueDays')} hint={t('settings.dueDaysHint')} right={<span className="bg-primary-soft text-primary rounded-lg px-2 py-1 text-xs font-bold">{t('settings.days', { count: s.defaultDueDays })}</span>}>
            {isOwner && (
              <>
                <div className="flex gap-2">
                  {DUE_PRESETS.map((d) => (
                    <button key={d} type="button" onClick={() => save({ defaultDueDays: d })} className={`h-10 flex-1 rounded-xl text-xs font-bold ${s.defaultDueDays === d ? 'bg-primary text-white' : 'bg-field'}`}>
                      {t('settings.days', { count: d })}
                    </button>
                  ))}
                  <button type="button" onClick={() => setCustomDue((v) => !v)} className={`h-10 flex-1 rounded-xl text-xs font-bold ${!DUE_PRESETS.includes(s.defaultDueDays) || customDue ? 'bg-primary-soft text-primary' : 'bg-field'}`}>
                    {t('settings.other')}
                  </button>
                </div>
                {customDue && (
                  <input
                    type="number"
                    inputMode="numeric"
                    min={1}
                    max={365}
                    defaultValue={s.defaultDueDays}
                    onBlur={(e) => {
                      const days = Number(e.target.value)
                      if (days >= 1 && days <= 365 && days !== s.defaultDueDays) save({ defaultDueDays: days })
                    }}
                    className={`${inputClass} mt-2`}
                    aria-label={t('settings.dueDays')}
                  />
                )}
              </>
            )}
          </SettingRow>

          <SettingRow
            icon={<Clock className="h-4 w-4" />}
            title={t('settings.reminderHour')}
            hint={t('settings.reminderHourHint')}
            right={
              isOwner ? (
                <select value={s.reminderHour} onChange={(e) => save({ reminderHour: Number(e.target.value) })} className="bg-field tabular h-9 rounded-xl px-2 text-xs font-bold outline-none" aria-label={t('settings.reminderHour')}>
                  {Array.from({ length: 24 }, (_, h) => (
                    <option key={h} value={h}>
                      {String(h).padStart(2, '0')}:00
                    </option>
                  ))}
                </select>
              ) : (
                <span className="tabular text-xs font-bold">{String(s.reminderHour).padStart(2, '0')}:00</span>
              )
            }
          />

          <SettingRow icon={<Bell className="h-4 w-4" />} title={t('settings.recipients')} hint={t('settings.recipientsHint')} right={isOwner ? <span /> : <span className="text-xs font-bold">{t(`settings.recipients.${s.reminderRecipients}`)}</span>}>
            {isOwner && (
              <SegmentedTabs
                value={s.reminderRecipients}
                onChange={(v) => save({ reminderRecipients: v })}
                options={[
                  { value: 'all', label: t('settings.recipients.all') },
                  { value: 'owners', label: t('settings.recipients.owners') },
                ]}
              />
            )}
          </SettingRow>

          <SettingRow icon={<MessageSquare className="h-4 w-4" />} title={t('settings.shopLanguage')} hint={t('settings.shopLanguageHint')} right={isOwner ? <span /> : <span className="text-xs font-bold">{t(`lang.${s.language}`)}</span>}>
            {isOwner && <SegmentedTabs value={s.language} onChange={(v: Language) => save({ language: v })} options={languageOptions} />}
          </SettingRow>

          <SettingRow icon={<Wallet className="h-4 w-4" />} title={t('settings.currency')} right={<span className="bg-field rounded-lg px-2 py-1 text-xs font-bold">UZS (so'm)</span>} />
        </SettingGroup>

        <SectionTitle>{t('settings.staff')}</SectionTitle>
        <SettingGroup>
          <SettingRow icon={<Users className="h-4 w-4" />} title={t('settings.staffList')} hint={t('settings.members', { count: s.membersCount })} onClick={() => navigate('/staff')} />
          {isOwner && <SettingRow icon={<History className="h-4 w-4" />} title={t('settings.audit')} hint={t('settings.auditHint')} onClick={() => navigate('/audit')} />}
        </SettingGroup>

        <SectionTitle>{t('settings.app')}</SectionTitle>
        <SettingGroup>
          <SettingRow icon={<Globe className="h-4 w-4" />} title={t('settings.language')} hint={t('settings.languageHint')} right={<span />}>
            <SegmentedTabs value={me.language} onChange={(l: Language) => void setLanguage(l).catch((e) => toast.error(e))} options={languageOptions} />
          </SettingRow>
          <SettingRow icon={<Send className="h-4 w-4" />} title={t('settings.bot')} hint={t('settings.botHint')} />
        </SettingGroup>

        <div className="mt-5">
          <SettingGroup>
            <SettingRow tone="danger" icon={<LogOut className="h-4 w-4" />} title={t('settings.leave')} onClick={() => void leave()} />
          </SettingGroup>
        </div>
      </div>

      <BottomSheet
        open={renaming}
        onClose={() => setRenaming(false)}
        title={t('settings.rename')}
        footer={
          <Button full size="lg" loading={update.isPending} onClick={() => name.trim() && save({ name: name.trim() }, () => setRenaming(false))}>
            {t('common.save')}
          </Button>
        }
      >
        <Field label={t('shops.name')}>
          <input value={name} maxLength={64} autoFocus onChange={(e) => setName(e.target.value)} className={inputClass} />
        </Field>
      </BottomSheet>
    </>
  )
}
