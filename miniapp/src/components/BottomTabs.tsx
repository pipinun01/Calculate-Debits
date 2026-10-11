import { ChartPie, ClipboardList, Settings, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router-dom'
import { haptic } from '../lib/telegram'

const tabs = [
  { to: '/customers', key: 'tabs.customers', Icon: Users },
  { to: '/debts', key: 'tabs.debts', Icon: ClipboardList },
  { to: '/reports', key: 'tabs.reports', Icon: ChartPie },
  { to: '/settings', key: 'tabs.settings', Icon: Settings },
] as const

export function BottomTabs({ overdue }: { overdue: boolean }) {
  const { t } = useTranslation()
  return (
    <nav className="bg-card/95 border-line fixed inset-x-0 bottom-0 z-40 border-t shadow-bar backdrop-blur-xl" style={{ paddingBottom: 'var(--safe-bottom)' }}>
      <div className="app-frame app-x flex h-16">
        {tabs.map(({ to, key, Icon }) => (
          <NavLink key={to} to={to} onClick={() => haptic('select')} className="group flex min-w-0 flex-1 flex-col items-center justify-center gap-0.5 transition active:scale-95">
            {({ isActive }) => (
              <>
                <span className={`relative flex h-8 w-12 items-center justify-center rounded-xl transition ${isActive ? 'bg-primary-soft text-accent' : 'text-muted'}`}>
                  <Icon className="h-5 w-5" strokeWidth={isActive ? 2.3 : 1.9} />
                  {to === '/debts' && overdue && <span className="bg-danger-fill ring-card absolute top-1 right-2.5 h-2 w-2 rounded-full ring-2" />}
                </span>
                <span className={`max-w-full truncate px-1 text-[11px] tracking-tight ${isActive ? 'text-accent font-bold' : 'text-muted font-medium'}`}>{t(key)}</span>
              </>
            )}
          </NavLink>
        ))}
      </div>
    </nav>
  )
}
