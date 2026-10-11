import { useState } from 'react'
import { AlarmClock, BadgeCheck, CalendarDays, ChevronRight, Download, FileSpreadsheet, Globe, History, LogOut, MailCheck, MessageSquare, Pencil, Send, Store, UserPlus, Wallet } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useMembers, useShop, useUpdateShop, type ShopInput } from '../api/queries'
import type { Language } from '../api/types'
import { useSession } from '../auth/Session'
import { BottomSheet } from '../components/BottomSheet'
import { Button } from '../components/Button'
import { SegmentedTabs } from '../components/Chips'
import { MemberRow, SettingGroup, SettingRow } from '../components/SettingRow'
import { PageTitle, ShopHeader } from '../components/ShopHeader'
import { ErrorState, Field, Loading, SectionTitle, inputClass } from '../components/States'
import { useToast } from '../hooks/useToast'
import { useToday } from '../hooks/useToday'
import { LANGUAGES } from '../i18n'
import { periodRange } from '../lib/dates'
import { exportFile } from '../lib/export'
import { closeApp, confirmDialog, inTelegram } from '../lib/telegram'

const DUE_PRESETS = [15, 30, 45]

export default function SettingsPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const toast = useToast()
  const { me, shop: membership, isOwner, setLanguage, reload } = useSession()
  const shop = useShop()
  const update = useUpdateShop()
  const members = useMembers()
  const today = useToday()
  const [exporting, setExporting] = useState(false)
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

  const exportShop = async () => {
    setExporting(true)
    try {
      const how = await exportFile('/api/export/shop', periodRange('year', today))
      toast.show(t(how === 'sent' ? 'reports.fileSent' : 'reports.downloaded'), 'success')
    } catch (e) {
      toast.error(e)
    } finally {
      setExporting(false)
    }
  }

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

  const pick = 'h-9 min-w-0 flex-1 rounded-xl px-2 text-xs transition active:scale-95'
  const picked = 'bg-primary font-bold text-white shadow-sm'
  const idle = 'bg-field text-muted font-semibold'
  const customPicked = !DUE_PRESETS.includes(s.defaultDueDays) || customDue

  return (
    <>
      <ShopHeader />
      <div className="app-x">
        <PageTitle title={t('settings.title')} subtitle={t('settings.subtitle')} />

        <section className="border-primary-line from-card to-primary-tint shadow-card relative mt-4 overflow-hidden rounded-3xl border bg-linear-to-br p-4">
          <div className="pointer-events-none absolute -top-12 -right-12 h-36 w-36 rounded-full bg-sky-300/25 blur-2xl dark:bg-sky-500/10" />
          <div className="relative flex items-center gap-3">
            <div className="bg-primary shadow-elevated flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl text-white">
              <Store className="h-6 w-6" />
            </div>
            <div className="min-w-0 flex-1">
              <h2 className="truncate text-base font-bold">{s.name}</h2>
              <p className="text-muted mt-0.5 truncate text-xs">
                {me.firstName} <span className="text-accent font-medium">({t(`role.${membership?.role ?? 'worker'}`)})</span>
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
                className="bg-card text-accent border-line flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border transition active:scale-95"
              >
                <Pencil className="h-4 w-4" />
              </button>
            )}
          </div>
          <div className="border-primary-line/60 relative mt-4 flex items-center justify-between gap-3 border-t pt-3">
            <span className="text-muted flex min-w-0 items-center gap-1.5 text-xs font-medium">
              <BadgeCheck className="text-success h-4 w-4 shrink-0" />
              <span className="truncate">{t('settings.members', { count: s.membersCount })}</span>
            </span>
            <button type="button" onClick={() => navigate('/shops')} className="text-accent flex shrink-0 items-center gap-0.5 text-xs font-semibold transition active:scale-95">
              {t('settings.switchShop')}
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </section>

        <div className="lg:grid lg:grid-cols-2 lg:items-start lg:gap-x-5">
          <div>
            <SectionTitle right={isOwner ? undefined : t('settings.ownerOnly')}>{t('settings.shop')}</SectionTitle>
            <SettingGroup>
              <SettingRow icon={<CalendarDays className="h-4 w-4" />} title={t('settings.dueDays')} hint={t('settings.dueDaysHint')} right={<span className="bg-primary-soft text-accent shrink-0 rounded-lg px-2 py-0.5 text-xs font-bold">{t('settings.days', { count: s.defaultDueDays })}</span>}>
                {isOwner && (
                  <>
                    <div className="flex gap-2">
                      {DUE_PRESETS.map((d) => (
                        <button key={d} type="button" onClick={() => save({ defaultDueDays: d })} className={`${pick} ${s.defaultDueDays === d && !customDue ? picked : idle}`}>
                          {t('settings.days', { count: d })}
                        </button>
                      ))}
                      <button type="button" onClick={() => setCustomDue((v) => !v)} className={`${pick} ${customPicked ? 'bg-primary-soft text-accent font-bold' : idle}`}>
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
                icon={<AlarmClock className="h-4 w-4" />}
                title={t('settings.reminderHour')}
                hint={t('settings.reminderHourHint')}
                right={
                  isOwner ? (
                    <select value={s.reminderHour} onChange={(e) => save({ reminderHour: Number(e.target.value) })} className="bg-field border-line tabular h-9 shrink-0 rounded-lg border px-2 text-sm font-bold outline-none" aria-label={t('settings.reminderHour')}>
                      {Array.from({ length: 24 }, (_, h) => (
                        <option key={h} value={h}>
                          {String(h).padStart(2, '0')}:00
                        </option>
                      ))}
                    </select>
                  ) : (
                    <span className="tabular shrink-0 text-xs font-bold">{String(s.reminderHour).padStart(2, '0')}:00</span>
                  )
                }
              />

              <SettingRow icon={<MailCheck className="h-4 w-4" />} title={t('settings.recipients')} hint={t('settings.recipientsHint')} right={isOwner ? <span /> : <span className="shrink-0 text-xs font-bold">{t(`settings.recipients.${s.reminderRecipients}`)}</span>}>
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

              <SettingRow icon={<MessageSquare className="h-4 w-4" />} title={t('settings.shopLanguage')} hint={t('settings.shopLanguageHint')} right={isOwner ? <span /> : <span className="shrink-0 text-xs font-bold">{t(`lang.${s.language}`)}</span>}>
                {isOwner && <SegmentedTabs tone="primary" value={s.language} onChange={(v: Language) => save({ language: v })} options={languageOptions} />}
              </SettingRow>

              <SettingRow icon={<Wallet className="h-4 w-4" />} title={t('settings.currency')} right={<span className="bg-field border-line shrink-0 rounded-lg border px-2.5 py-1 text-xs font-bold">UZS (so'm)</span>} />
            </SettingGroup>
          </div>

          <div>
            <SectionTitle right={<span className="text-accent font-semibold">{t('settings.members', { count: s.membersCount })}</span>}>{t('settings.staff')}</SectionTitle>
            <SettingGroup>
              {members.data?.map((m) => <MemberRow key={m.id} member={m} roleLabel={t(`role.${m.role}`)} youLabel={t('staff.you')} onClick={() => navigate('/staff')} />)}
              <SettingRow tone="accent" icon={<UserPlus className="h-4 w-4" />} title={t(isOwner ? 'staff.invite' : 'settings.staffList')} hint={isOwner ? t('settings.inviteHint') : undefined} onClick={() => navigate('/staff')} />
            </SettingGroup>

            <SectionTitle>{t('settings.app')}</SectionTitle>
            <SettingGroup>
              <SettingRow icon={<Globe className="h-4 w-4" />} title={t('settings.language')} hint={t('settings.languageHint')} right={<span />}>
                <SegmentedTabs tone="primary" value={me.language} onChange={(l: Language) => void setLanguage(l).catch((e) => toast.error(e))} options={languageOptions} />
              </SettingRow>
              <SettingRow icon={<Send className="h-4 w-4" />} title={t('settings.bot')} hint={t('settings.botHint')} />
            </SettingGroup>

            {isOwner && (
              <>
                <SectionTitle>{t('settings.archive')}</SectionTitle>
                <SettingGroup>
                  <SettingRow icon={<FileSpreadsheet className="h-4 w-4" />} title={t('settings.export')} hint={t('settings.exportHint')} onClick={exporting ? undefined : () => void exportShop()} right={<Download className={`text-muted h-4 w-4 shrink-0 ${exporting ? 'animate-pulse' : ''}`} />} />
                  <SettingRow icon={<History className="h-4 w-4" />} title={t('settings.audit')} hint={t('settings.auditHint')} onClick={() => navigate('/audit')} />
                </SettingGroup>
              </>
            )}
          </div>
        </div>

        <div className="mt-6 space-y-4 pb-4 text-center">
          <Button full variant="danger" size="lg" className="text-sm" icon={<LogOut className="h-5 w-5" />} onClick={() => void leave()}>
            {t('settings.leave')}
          </Button>
          <p className="text-muted text-[11px]">Debits · Telegram Mini App</p>
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
