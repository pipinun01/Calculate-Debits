import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, getShopId, query } from './client'
import type {
  AuditEvent,
  CustomerDetail,
  CustomerFilter,
  CustomerListItem,
  DebtStatus,
  DebtSummary,
  DebtsResponse,
  EntryParse,
  Invite,
  LedgerMutation,
  Member,
  Page,
  ReminderRecipients,
  ReportSummary,
  Role,
  Shop,
  Transaction,
  TxType,
  Language,
} from './types'

/** Every shop-scoped key starts with the shop id, so switching shops never shows another shop's cache. */
const key = (...parts: unknown[]) => ['shop', getShopId(), ...parts]

/** After any write the whole shop cache is stale (balances, summaries, lists all depend on the ledger). */
function useInvalidateShop() {
  const client = useQueryClient()
  return () => client.invalidateQueries({ queryKey: ['shop', getShopId()] })
}

export const useShop = () => useQuery({ queryKey: key('shop'), queryFn: () => api<Shop>('GET', '/api/shop') })

export const useDebtSummary = () =>
  useQuery({ queryKey: key('summary'), queryFn: () => api<DebtSummary>('GET', '/api/debts/summary') })

export const useCustomers = (filter: CustomerFilter, q: string) =>
  useInfiniteQuery({
    queryKey: key('customers', filter, q),
    queryFn: ({ pageParam }) =>
      api<Page<CustomerListItem>>('GET', `/api/customers${query({ filter, q, cursor: pageParam, limit: 30, sort: 'name' })}`),
    initialPageParam: '' as string,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })

export const useCustomer = (id: string) =>
  useQuery({ queryKey: key('customer', id), queryFn: () => api<CustomerDetail>('GET', `/api/customers/${id}`) })

export const useLedger = (id: string) =>
  useInfiniteQuery({
    queryKey: key('ledger', id),
    queryFn: ({ pageParam }) =>
      api<Page<Transaction>>('GET', `/api/customers/${id}/ledger${query({ cursor: pageParam, limit: 30 })}`),
    initialPageParam: '' as string,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })

export const useDebts = (status: DebtStatus) =>
  useInfiniteQuery({
    queryKey: key('debts', status),
    queryFn: ({ pageParam }) => api<DebtsResponse>('GET', `/api/debts${query({ status, cursor: pageParam, limit: 30 })}`),
    initialPageParam: '' as string,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })

export const useReport = (from: string | undefined, to: string | undefined, enabled = true) =>
  useQuery({
    queryKey: key('report', from, to),
    queryFn: () => api<ReportSummary>('GET', `/api/reports/summary${query({ from, to })}`),
    enabled,
  })

export const useMembers = () => useQuery({ queryKey: key('members'), queryFn: () => api<Member[]>('GET', '/api/members') })

export const useInvites = (enabled: boolean) =>
  useQuery({ queryKey: key('invites'), queryFn: () => api<Invite[]>('GET', '/api/invites'), enabled })

export const useAudit = (enabled: boolean) =>
  useInfiniteQuery({
    queryKey: key('audit'),
    queryFn: ({ pageParam }) => api<Page<AuditEvent>>('GET', `/api/audit${query({ cursor: pageParam, limit: 30 })}`),
    initialPageParam: '' as string,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    enabled,
  })

export interface CustomerInput {
  name?: string
  tag?: string
  phone?: string
  notes?: string
  isArchived?: boolean
}

export function useCreateCustomer() {
  const invalidate = useInvalidateShop()
  return useMutation({
    mutationFn: (input: CustomerInput) => api<CustomerDetail>('POST', '/api/customers', { ...input }),
    onSuccess: invalidate,
  })
}

export function useUpdateCustomer(id: string) {
  const invalidate = useInvalidateShop()
  return useMutation({
    mutationFn: (input: CustomerInput) => api<CustomerDetail>('PATCH', `/api/customers/${id}`, { ...input }),
    onSuccess: invalidate,
  })
}

export interface TransactionInput {
  customerId: string
  type: TxType
  amount: number
  comment?: string
  dueDate?: string
  targetDebtId?: string
}

export function useCreateTransaction() {
  const invalidate = useInvalidateShop()
  return useMutation({
    mutationFn: (input: TransactionInput) => api<LedgerMutation>('POST', '/api/transactions', { ...input }),
    onSuccess: invalidate,
  })
}

export function useDeleteTransaction() {
  const invalidate = useInvalidateShop()
  return useMutation({
    mutationFn: (id: string) => api<{ id: string; balance: number }>('DELETE', `/api/transactions/${id}`),
    onSuccess: invalidate,
  })
}

export interface ShopInput {
  name?: string
  defaultDueDays?: number
  language?: Language
  reminderRecipients?: ReminderRecipients
  reminderHour?: number
}

export function useUpdateShop() {
  const invalidate = useInvalidateShop()
  return useMutation({ mutationFn: (input: ShopInput) => api<Shop>('PATCH', '/api/shop', { ...input }), onSuccess: invalidate })
}

export function useCreateInvite() {
  const invalidate = useInvalidateShop()
  return useMutation({ mutationFn: (role: Role) => api<Invite>('POST', '/api/invites', { role }), onSuccess: invalidate })
}

export function useRevokeInvite() {
  const invalidate = useInvalidateShop()
  return useMutation({ mutationFn: (id: string) => api<void>('DELETE', `/api/invites/${id}`), onSuccess: invalidate })
}

export function useChangeRole() {
  const invalidate = useInvalidateShop()
  return useMutation({
    mutationFn: ({ id, role }: { id: string; role: Role }) => api<Member>('PATCH', `/api/members/${id}`, { role }),
    onSuccess: invalidate,
  })
}

export function useRemoveMember() {
  const invalidate = useInvalidateShop()
  return useMutation({ mutationFn: (id: string) => api<void>('DELETE', `/api/members/${id}`), onSuccess: invalidate })
}

/** Recording → parsed entry (speech-to-text + the same parser as the bot). Nothing is saved. */
export function parseEntryAudio(blob: Blob, fileName: string): Promise<EntryParse> {
  const form = new FormData()
  form.append('audio', blob, fileName)
  return api<EntryParse>('POST', '/api/entries/parse', form)
}
