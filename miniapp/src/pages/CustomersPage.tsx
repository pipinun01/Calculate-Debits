import { useState } from 'react'
import { Plus, Users } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useCustomers, useDebtSummary } from '../api/queries'
import type { CustomerFilter } from '../api/types'
import { BalanceHero } from '../components/BalanceHero'
import { FAB } from '../components/Button'
import { FilterChips } from '../components/Chips'
import { CustomerFormSheet } from '../components/CustomerFormSheet'
import { CustomerRow } from '../components/CustomerRow'
import { SearchBar } from '../components/SearchBar'
import { ShopHeader } from '../components/ShopHeader'
import { Empty, ErrorState, Loading, SectionTitle } from '../components/States'
import { useDebounced } from '../hooks/useDebounced'
import { useInfiniteScroll } from '../hooks/useInfiniteScroll'
import { useToday } from '../hooks/useToday'

export default function CustomersPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const today = useToday()
  const [filter, setFilter] = useState<CustomerFilter>('all')
  const [search, setSearch] = useState('')
  const [adding, setAdding] = useState(false)
  const q = useDebounced(search.trim())

  const summary = useDebtSummary()
  const customers = useCustomers(filter, q)
  const items = customers.data?.pages.flatMap((p) => p.items) ?? []
  const sentinel = useInfiniteScroll<HTMLDivElement>(Boolean(customers.hasNextPage) && !customers.isFetchingNextPage, () => void customers.fetchNextPage())

  const s = summary.data
  return (
    <>
      <ShopHeader />
      <div className="app-x pt-3">
        <BalanceHero total={s?.total ?? 0} activeCustomers={s?.activeCustomers ?? 0} overdueCount={s?.overdueCount ?? 0} />

        <div className="mt-4 space-y-2.5">
          <SearchBar value={search} onChange={setSearch} placeholder={t('customers.search')} />
          <FilterChips<CustomerFilter>
            value={filter}
            onChange={setFilter}
            options={[
              { value: 'all', label: s ? `${t('customers.filter.all')} (${s.activeCustomers})` : t('customers.filter.all') },
              { value: 'debtors', label: s ? `${t('customers.filter.debtors')} (${s.debtorsCount})` : t('customers.filter.debtors') },
              { value: 'due_soon', label: t('customers.filter.dueSoon') },
              { value: 'archived', label: t('customers.filter.archived') },
            ]}
          />
        </div>

        <SectionTitle>{t('customers.list')}</SectionTitle>
        {customers.isPending ? (
          <Loading />
        ) : customers.isError ? (
          <ErrorState onRetry={() => void customers.refetch()} />
        ) : items.length === 0 ? (
          <Empty
            icon={<Users className="h-6 w-6" />}
            title={q ? t('customers.notFound', { q }) : t('customers.empty')}
            hint={q || filter !== 'all' ? undefined : t('customers.emptyHint')}
          />
        ) : (
          <>
            <div className="card-grid">
              {items.map((c) => (
                <CustomerRow key={c.id} customer={c} today={today} />
              ))}
            </div>
            <div ref={sentinel} />
            {customers.isFetchingNextPage && <Loading />}
          </>
        )}
      </div>

      <FAB label={t('customer.new')} onClick={() => setAdding(true)}>
        <Plus className="h-7 w-7" strokeWidth={2.4} />
      </FAB>
      {adding && <CustomerFormSheet onClose={() => setAdding(false)} initialName={items.length === 0 ? search.trim() : ''} onSaved={(c) => navigate(`/customers/${c.id}`)} />}
    </>
  )
}
