import { CircleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { ApiError } from './api/client'
import { useDebtSummary } from './api/queries'
import { SessionProvider, useSession } from './auth/Session'
import { BottomTabs } from './components/BottomTabs'
import { Button } from './components/Button'
import { Empty, Loading } from './components/States'
import { inTelegram } from './lib/telegram'
import AuditPage from './pages/AuditPage'
import CustomerPage from './pages/CustomerPage'
import CustomersPage from './pages/CustomersPage'
import DebtsPage from './pages/DebtsPage'
import ReportsPage from './pages/ReportsPage'
import SettingsPage from './pages/SettingsPage'
import ShopsPage from './pages/ShopsPage'
import StaffPage from './pages/StaffPage'

/** The four tabs share the bottom bar; the red dot on "Debts" means something is overdue. */
function TabsLayout() {
  const summary = useDebtSummary()
  return (
    <div className="mx-auto min-h-full max-w-lg" style={{ paddingBottom: 'calc(88px + var(--safe-bottom))' }}>
      <Outlet />
      <BottomTabs overdue={(summary.data?.overdueCount ?? 0) > 0} />
    </div>
  )
}

function InnerLayout() {
  return (
    <div className="mx-auto min-h-full max-w-lg">
      <Outlet />
    </div>
  )
}

function Shell() {
  const { shop } = useSession()
  // No shop yet: the only thing to do is create one (or join by an invite link in the bot).
  if (!shop) {
    return (
      <div className="mx-auto min-h-full max-w-lg">
        <ShopsPage />
      </div>
    )
  }

  // Keyed by shop: switching shops remounts every screen with clean state.
  return (
    <Routes key={shop.shopId}>
      <Route element={<TabsLayout />}>
        <Route path="/customers" element={<CustomersPage />} />
        <Route path="/debts" element={<DebtsPage />} />
        <Route path="/reports" element={<ReportsPage />} />
        <Route path="/settings" element={<SettingsPage />} />
      </Route>
      <Route element={<InnerLayout />}>
        <Route path="/customers/:id" element={<CustomerPage />} />
        <Route path="/staff" element={<StaffPage />} />
        <Route path="/audit" element={<AuditPage />} />
        <Route path="/shops" element={<ShopsPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/customers" replace />} />
    </Routes>
  )
}

function StartError({ retry, error }: { retry: () => void; error: unknown }) {
  const { t } = useTranslation()
  const outside = !inTelegram() && !import.meta.env.DEV
  const code = error instanceof ApiError ? error.code : 'network'
  return (
    <div className="flex min-h-full items-center justify-center">
      <Empty
        icon={<CircleAlert className="h-6 w-6" />}
        title={outside ? t('start.openInTelegram') : t(`errors.${code}`, { defaultValue: t('errors.generic') })}
        hint={outside ? t('start.openInTelegramHint') : undefined}
        action={!outside && <Button onClick={retry}>{t('common.retry')}</Button>}
      />
    </div>
  )
}

export default function App() {
  return (
    <SessionProvider loading={<Loading full />} error={(retry, error) => <StartError retry={retry} error={error} />}>
      <Shell />
    </SessionProvider>
  )
}
