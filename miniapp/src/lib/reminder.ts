import type { Language } from '../api/types'
import { formatDate } from './dates'
import { formatUzs } from './formatUzs'

interface ReminderInput {
  language: Language
  customerName: string
  shopName: string
  amount: number
  dueDate: string
  /** Negative = already overdue. */
  daysLeft: number
}

const templates: Record<Language, { overdue: string; upcoming: string }> = {
  'uz-Latn': {
    overdue:
      "Assalomu alaykum, {name}! «{shop}» do'konidan eslatma: {amount} so'm qarzingizning muddati {date} kuni o'tgan. Iltimos, imkon topib to'lab qo'ysangiz. Rahmat!",
    upcoming:
      "Assalomu alaykum, {name}! «{shop}» do'konidan eslatma: {amount} so'm qarzingizni {date} gacha to'lashingizni so'raymiz. Rahmat!",
  },
  'uz-Cyrl': {
    overdue:
      'Ассалому алайкум, {name}! «{shop}» дўконидан эслатма: {amount} сўм қарзингизнинг муддати {date} куни ўтган. Илтимос, имкон топиб тўлаб қўйсангиз. Раҳмат!',
    upcoming:
      'Ассалому алайкум, {name}! «{shop}» дўконидан эслатма: {amount} сўм қарзингизни {date} гача тўлашингизни сўраймиз. Раҳмат!',
  },
  ru: {
    overdue:
      'Здравствуйте, {name}! Напоминание от магазина «{shop}»: срок вашего долга {amount} сум истёк {date}. Пожалуйста, оплатите при возможности. Спасибо!',
    upcoming:
      'Здравствуйте, {name}! Напоминание от магазина «{shop}»: просим оплатить долг {amount} сум до {date}. Спасибо!',
  },
}

/** Message for the debtor in the shop's language (not the app user's): the shopkeeper forwards it from their own account. */
export function reminderText(input: ReminderInput): string {
  const template = templates[input.language] ?? templates['uz-Latn']
  return (input.daysLeft < 0 ? template.overdue : template.upcoming)
    .replace('{name}', input.customerName)
    .replace('{shop}', input.shopName)
    .replace('{amount}', formatUzs(input.amount).replace(/\u00a0/g, ' '))
    .replace('{date}', formatDate(input.dueDate))
}
