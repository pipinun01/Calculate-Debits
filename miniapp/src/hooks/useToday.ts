import { useShop } from '../api/queries'
import { todayIn } from '../lib/dates'

/** Today's date in the shop's time zone (falls back to Tashkent until the shop is loaded). */
export function useToday(): string {
  const { data: shop } = useShop()
  return todayIn(shop?.timezone ?? 'Asia/Tashkent')
}
