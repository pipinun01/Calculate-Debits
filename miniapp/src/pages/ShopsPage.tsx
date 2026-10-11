import { useState } from 'react'
import { Check, Plus, Store } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { CreateShopResponse } from '../api/types'
import { useSession } from '../auth/Session'
import { BottomSheet } from '../components/BottomSheet'
import { Button } from '../components/Button'
import { PageHeader } from '../components/ShopHeader'
import { Empty, Field, SectionTitle, inputClass } from '../components/States'
import { useBackButton } from '../hooks/useBackButton'
import { useToast } from '../hooks/useToast'

const DUE_PRESETS = [15, 30, 45]

/** Shop switcher; also the first screen for a user who has no shop yet. */
export default function ShopsPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const toast = useToast()
  const { me, shop, switchShop, reload } = useSession()
  const onboarding = me.shops.length === 0
  useBackButton(!onboarding)

  const [creating, setCreating] = useState(false)
  const [name, setName] = useState('')
  const [dueDays, setDueDays] = useState(30)
  const [busy, setBusy] = useState<string | null>(null)

  const choose = async (shopId: string) => {
    if (shopId === shop?.shopId) {
      navigate('/customers')
      return
    }
    setBusy(shopId)
    try {
      await switchShop(shopId)
      navigate('/customers')
    } catch (e) {
      toast.error(e)
    } finally {
      setBusy(null)
    }
  }

  const create = async () => {
    if (!name.trim()) {
      toast.show(t('shops.needName'), 'error')
      return
    }
    setBusy('new')
    try {
      await api<CreateShopResponse>('POST', '/api/shops', { name: name.trim(), defaultDueDays: dueDays, language: me.language })
      await reload()
      setCreating(false)
      setName('')
      navigate('/customers')
    } catch (e) {
      toast.error(e)
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="pb-8">
      <PageHeader title={t('shops.title')} subtitle={me.firstName} />
      <div className="app-x pt-1">
        {onboarding ? (
          <Empty icon={<Store className="h-6 w-6" />} title={t('shops.none')} hint={t('shops.noneHint')} />
        ) : (
          <>
            <SectionTitle>{t('shops.yours')}</SectionTitle>
            <div className="space-y-2.5">
              {me.shops.map((s) => {
                const active = s.shopId === shop?.shopId
                return (
                  <button key={s.shopId} type="button" disabled={busy !== null} onClick={() => void choose(s.shopId)} className={`bg-card border-line shadow-soft rounded-card border flex w-full items-center gap-3 border-2 p-4 text-left ${active ? 'border-primary' : 'border-transparent'}`}>
                    <div className="bg-primary-soft text-accent flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl">
                      <Store className="h-5 w-5" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-extrabold">{s.shopName}</p>
                      <p className="text-muted text-xs">{t(`role.${s.role}`)}</p>
                    </div>
                    {active && (
                      <span className="bg-primary flex h-6 w-6 items-center justify-center rounded-full text-white">
                        <Check className="h-3.5 w-3.5" />
                      </span>
                    )}
                  </button>
                )
              })}
            </div>
          </>
        )}

        <Button full variant={onboarding ? 'primary' : 'soft'} className="mt-4" icon={<Plus className="h-4 w-4" />} onClick={() => setCreating(true)}>
          {t('shops.create')}
        </Button>
      </div>

      <BottomSheet
        open={creating}
        onClose={() => setCreating(false)}
        title={t('shops.create')}
        footer={
          <Button full size="lg" loading={busy === 'new'} onClick={() => void create()}>
            {t('common.save')}
          </Button>
        }
      >
        <Field label={t('shops.name')}>
          <input value={name} maxLength={64} autoFocus onChange={(e) => setName(e.target.value)} placeholder="Aloqa bozori" className={inputClass} />
        </Field>
        <Field label={t('settings.dueDays')} hint={t('settings.dueDaysHint')}>
          <div className="flex gap-2">
            {DUE_PRESETS.map((d) => (
              <button key={d} type="button" onClick={() => setDueDays(d)} className={`h-11 flex-1 rounded-xl text-sm font-bold ${dueDays === d ? 'bg-primary text-white' : 'bg-card border-line border'}`}>
                {t('settings.days', { count: d })}
              </button>
            ))}
          </div>
        </Field>
      </BottomSheet>
    </div>
  )
}
