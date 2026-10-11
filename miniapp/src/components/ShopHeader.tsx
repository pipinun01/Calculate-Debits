import type { ReactNode } from 'react'
import { ArrowLeftRight, ChevronDown, ChevronLeft } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/Session'
import { inTelegram } from '../lib/telegram'

const bar = 'bg-bg/90 border-line/70 sticky top-0 z-30 border-b backdrop-blur-md'

/** Top bar of the four main tabs: the active shop (tap to switch) and an optional action on the right. */
export function ShopHeader({ right }: { right?: ReactNode }) {
  const { shop } = useSession()
  const { t } = useTranslation()
  return (
    <header className={bar}>
      <div className="app-x flex items-center justify-between gap-3 pt-3 pb-2.5">
        <Link to="/shops" className="bg-card border-line shadow-soft flex h-9 min-w-0 items-center gap-2 rounded-full border px-3 transition active:scale-95">
          <span className="bg-success-fill ring-success-fill/20 h-2 w-2 shrink-0 animate-pulse rounded-full ring-2" />
          <span className="truncate text-xs font-bold tracking-tight">{shop?.shopName}</span>
          <ChevronDown className="text-muted h-3.5 w-3.5 shrink-0" />
        </Link>
        <div className="flex shrink-0 items-center gap-1.5">
          {right}
          <Link to="/shops" aria-label={t('settings.switchShop')} className="bg-card border-line text-muted shadow-soft flex h-9 w-9 items-center justify-center rounded-full border transition active:scale-95">
            <ArrowLeftRight className="h-4 w-4" />
          </Link>
        </div>
      </div>
    </header>
  )
}

/** Title bar of inner screens. The arrow is drawn only outside Telegram, where there is no native back button. */
export function PageHeader({ title, subtitle, right }: { title: ReactNode; subtitle?: ReactNode; right?: ReactNode }) {
  const navigate = useNavigate()
  return (
    <header className={bar}>
      <div className="app-x flex min-h-14 items-center gap-2.5 py-2">
        {!inTelegram() && (
          <button type="button" onClick={() => navigate(-1)} aria-label="Back" className="text-accent border-line bg-card flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border transition active:scale-95">
            <ChevronLeft className="h-5 w-5" />
          </button>
        )}
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-sm leading-tight font-bold md:text-base">{title}</h1>
          {subtitle && <p className="text-muted truncate text-xs font-medium">{subtitle}</p>}
        </div>
        {right}
      </div>
    </header>
  )
}

/** Big screen title under the top bar of a tab. */
export function PageTitle({ title, subtitle, right }: { title: ReactNode; subtitle?: ReactNode; right?: ReactNode }) {
  return (
    <div className="flex items-end justify-between gap-3 pt-3">
      <div className="min-w-0">
        <h1 className="truncate text-2xl font-extrabold tracking-tight">{title}</h1>
        {subtitle && <p className="text-muted mt-0.5 truncate text-xs font-medium">{subtitle}</p>}
      </div>
      {right}
    </div>
  )
}
