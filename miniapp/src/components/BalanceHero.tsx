import { TriangleAlert, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { formatUzs } from '../lib/formatUzs'

interface Props {
  total: number
  activeCustomers: number
  overdueCount: number
}

/** Gradient card on the customers tab: everything the shop is owed right now. */
export function BalanceHero({ total, activeCustomers, overdueCount }: Props) {
  const { t } = useTranslation()
  return (
    <section className="hero-gradient shadow-elevated rounded-card p-5 text-white">
      <p className="text-[11px] font-bold tracking-widest uppercase opacity-80">{t('hero.title')}</p>
      <p className="tabular mt-1 text-[32px] leading-tight font-extrabold">
        {formatUzs(total)} <span className="text-sm font-bold opacity-80">UZS</span>
      </p>
      <div className="mt-4 flex flex-wrap gap-2">
        <span className="inline-flex items-center gap-1.5 rounded-full bg-white/20 px-3 py-1.5 text-xs font-bold">
          <Users className="h-3.5 w-3.5" />
          {t('hero.active', { count: activeCustomers })}
        </span>
        {overdueCount > 0 && (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-rose-500/80 px-3 py-1.5 text-xs font-bold">
            <TriangleAlert className="h-3.5 w-3.5" />
            {t('hero.overdue', { count: overdueCount })}
          </span>
        )}
      </div>
    </section>
  )
}
