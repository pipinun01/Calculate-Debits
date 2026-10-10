import type { ReactNode } from 'react'
import { ChevronDown, ChevronLeft } from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'
import { useSession } from '../auth/Session'
import { inTelegram } from '../lib/telegram'

/** Top bar of the four main tabs: the active shop (tap to switch) and an optional action on the right. */
export function ShopHeader({ right }: { right?: ReactNode }) {
  const { shop } = useSession()
  return (
    <header className="flex items-center justify-between gap-3 px-4 pt-3 pb-2">
      <Link to="/shops" className="bg-card border-line shadow-soft flex h-10 min-w-0 items-center gap-2 rounded-full border pr-3 pl-3.5">
        <span className="bg-success h-2 w-2 shrink-0 rounded-full" />
        <span className="truncate text-sm font-bold">{shop?.shopName}</span>
        <ChevronDown className="text-muted h-4 w-4 shrink-0" />
      </Link>
      {right}
    </header>
  )
}

/** Title bar of inner screens. The arrow is drawn only outside Telegram, where there is no native back button. */
export function PageHeader({ title, subtitle, right }: { title: ReactNode; subtitle?: ReactNode; right?: ReactNode }) {
  const navigate = useNavigate()
  return (
    <header className="flex items-center gap-3 px-4 pt-3 pb-2">
      {!inTelegram() && (
        <button type="button" onClick={() => navigate(-1)} aria-label="Back" className="bg-card border-line flex h-10 w-10 shrink-0 items-center justify-center rounded-full border">
          <ChevronLeft className="h-5 w-5" />
        </button>
      )}
      <div className="min-w-0 flex-1">
        <h1 className="truncate text-lg leading-tight font-extrabold">{title}</h1>
        {subtitle && <p className="text-muted truncate text-xs">{subtitle}</p>}
      </div>
      {right}
    </header>
  )
}
