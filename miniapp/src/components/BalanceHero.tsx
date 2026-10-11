import { TriangleAlert } from 'lucide-react'
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
    <section className="hero-gradient relative overflow-hidden rounded-3xl p-5 text-white">
      <div className="pointer-events-none absolute -top-8 -right-8 h-40 w-40 rounded-full bg-white/10 blur-xl" />
      <div className="pointer-events-none absolute -bottom-10 -left-10 h-36 w-36 rounded-full bg-sky-300/20 blur-lg" />
      <div className="relative">
        <p className="text-[11px] font-bold tracking-wider text-white/85 uppercase">{t('hero.title')}</p>
        <p className="tabular mt-1 flex flex-wrap items-baseline gap-x-2 text-3xl leading-tight font-extrabold tracking-tight md:text-4xl">
          {formatUzs(total)} <span className="text-sm font-bold text-white/75">UZS</span>
        </p>
        <div className="mt-4 flex flex-wrap gap-2">
          <span className="hero-chip inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-[11px] font-semibold">
            <span className="h-1.5 w-1.5 rounded-full bg-white dark:bg-sky-300" />
            {t('hero.active', { count: activeCustomers })}
          </span>
          {overdueCount > 0 && (
            <span className="inline-flex items-center gap-1.5 rounded-full border border-rose-300/30 bg-rose-500/35 px-3 py-1 text-[11px] font-semibold text-rose-50 backdrop-blur dark:bg-rose-500/20 dark:text-rose-200">
              <TriangleAlert className="h-3.5 w-3.5 shrink-0" />
              {t('hero.overdue', { count: overdueCount })}
            </span>
          )}
        </div>
      </div>
    </section>
  )
}
