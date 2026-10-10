/** DTOs of the backend REST API (camelCase JSON; enums are camelCase strings; language is the locale code). */

export type Language = 'uz-Latn' | 'uz-Cyrl' | 'ru'
export type Role = 'owner' | 'worker'
export type TxType = 'debt' | 'payment'
export type ReminderRecipients = 'all' | 'owners'

export interface Membership {
  shopId: string
  shopName: string
  memberId: string
  role: Role
  isActive: boolean
}

export interface Me {
  id: string
  telegramUserId: number
  firstName: string
  username: string | null
  language: Language
  activeShopId: string | null
  shops: Membership[]
}

export interface AuthResponse {
  token: string
  expiresAt: string
  user: Me
}

export interface Shop {
  id: string
  name: string
  defaultDueDays: number
  language: Language
  timezone: string
  reminderRecipients: ReminderRecipients
  reminderHour: number
  membersCount: number
  createdAt: string
}

export interface Page<T> {
  items: T[]
  nextCursor: string | null
}

export interface CustomerListItem {
  id: string
  name: string
  tag: string | null
  phone: string | null
  balance: number
  nextDueDate: string | null
  overdueAmount: number
  dueSoonAmount: number
  isArchived: boolean
  createdAt: string
}

export interface Transaction {
  id: string
  customerId: string
  type: TxType
  amount: number
  comment: string | null
  dueDate: string | null
  targetDebtId: string | null
  source: 'manual' | 'voice' | 'text'
  createdByMemberId: string
  createdByName: string
  createdAt: string
  updatedAt: string | null
  remaining: number | null
  canEdit: boolean
}

export interface CustomerOpenDebt {
  debtId: string
  amount: number
  remaining: number
  dueDate: string
  daysLeft: number
  createdAt: string
  comment: string | null
  createdByName: string
}

export interface CustomerDetail {
  id: string
  name: string
  tag: string | null
  phone: string | null
  notes: string | null
  balance: number
  nextDueDate: string | null
  overdueAmount: number
  dueSoonAmount: number
  isArchived: boolean
  createdAt: string
  createdByName: string | null
  lastDebt: Transaction | null
  lastPayment: Transaction | null
  openDebts: CustomerOpenDebt[]
}

export interface LedgerMutation {
  transaction: Transaction
  balance: number
}

export interface DebtSummary {
  today: string
  total: number
  overdue: number
  dueSoon: number
  onTime: number
  overdueCount: number
  dueSoonCount: number
  debtorsCount: number
  activeCustomers: number
}

export interface OpenDebt {
  debtId: string
  customerId: string
  customerName: string
  customerTag: string | null
  customerPhone: string | null
  amount: number
  remaining: number
  dueDate: string
  daysLeft: number
  comment: string | null
  createdByName: string
  createdAt: string
}

export interface DebtsResponse {
  summary: DebtSummary
  items: OpenDebt[]
  nextCursor: string | null
}

export type DebtStatus = 'open' | 'overdue' | 'due_soon' | 'on_time'
export type CustomerFilter = 'all' | 'debtors' | 'due_soon' | 'archived'

export interface FlowPoint {
  from: string
  to: string
  debts: number
  payments: number
}

export interface TopDebtor {
  customerId: string
  name: string
  tag: string | null
  phone: string | null
  balance: number
  overdue: number
  nextDueDate: string | null
}

export interface ReportSummary {
  from: string
  to: string
  collected: number
  collectedPrevious: number
  newDebts: number
  newDebtsPrevious: number
  paymentsCount: number
  debtsCount: number
  collectionRate: number
  openTotal: number
  overdueTotal: number
  flow: FlowPoint[]
  topDebtors: TopDebtor[]
}

export interface Member {
  id: string
  userId: string
  telegramUserId: number
  firstName: string
  username: string | null
  role: Role
  notificationsBlocked: boolean
  createdAt: string
  isMe: boolean
}

export interface Invite {
  id: string
  role: Role
  expiresAt: string
  createdAt: string
  createdByName: string | null
  link: string
}

export interface EntryParse {
  transcript: string
  type: TxType
  amount: number | null
  dueDate: string | null
  dueDateIsDefault: boolean
  comment: string | null
  customerName: string | null
  customerStatus: 'exact' | 'ambiguous' | 'none'
  customerId: string | null
  warnings: string[]
}

export interface CreateShopResponse {
  shop: Shop
  membership: Membership
}

export interface AuditEvent {
  id: number
  action: string
  entity: string
  memberName: string | null
  createdAt: string
  /** Customer or staff member the action was about. */
  subject: string | null
  amount: number | null
  transactionType: TxType | null
}
