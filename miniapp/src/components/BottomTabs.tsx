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
    <nav className="bg-card border-line fixed inset-x-0 bottom-0 z-40 border-t" style={{ paddingBottom: 'var(--safe-bottom)' }}>
      <div className="mx-auto flex h-16 max-w-lg">
        {tabs.map(({ to, key, Icon }) => (
          <NavLink
            key={to}
            to={to}
            onClick={() => haptic('select')}
            className={({ isActive }) => `relative flex flex-1 flex-col items-center justify-center gap-1 text-[10px] font-bold ${isActive ? 'text-primary' : 'text-muted'}`}
          >
            <span className="relative">
              <Icon className="h-5 w-5" />
              {to === '/debts' && overdue && <span className="bg-danger ring-card absolute -top-0.5 -right-1 h-2 w-2 rounded-full ring-2" />}
            </span>
            {t(key)}
          </NavLink>
        ))}
      </div>
    </nav>
  )
}
