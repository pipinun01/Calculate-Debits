/** 1250000 -> "1 250 000" (narrow no-break spaces as thousands separators). */
export function formatUzs(amount: number | bigint): string {
  const s = BigInt(amount).toString()
  const neg = s.startsWith('-')
  const digits = neg ? s.slice(1) : s
  const grouped = digits.replace(/\B(?=(\d{3})+(?!\d))/g, ' ')
  return neg ? `-${grouped}` : grouped
}
