/** 1250000 -> "1 250 000". The separator is a no-break space: amounts never wrap, and unlike the narrow variant it stays visible in every font. */
export function formatUzs(amount: number | bigint): string {
  const s = BigInt(Math.trunc(Number(amount))).toString()
  const neg = s.startsWith('-')
  const digits = neg ? s.slice(1) : s
  const grouped = digits.replace(/\B(?=(\d{3})+(?!\d))/g, '\u00a0')
  return neg ? `-${grouped}` : grouped
}

/** 45200000 -> "45.2M", 550000 -> "550K", 900 -> "900". For chart centres and axis labels only; money in lists is always full. */
export function formatCompact(amount: number): string {
  const abs = Math.abs(amount)
  const sign = amount < 0 ? '-' : ''
  const trim = (n: number) => n.toFixed(1).replace(/\.0$/, '')
  if (abs >= 1_000_000_000) return `${sign}${trim(abs / 1_000_000_000)}B`
  if (abs >= 1_000_000) return `${sign}${trim(abs / 1_000_000)}M`
  if (abs >= 1_000) return `${sign}${trim(abs / 1_000)}K`
  return `${sign}${abs}`
}

/** What the user typed ("550 000", "550.000so'm") → integer so'm, capped at the backend limit. */
export function parseAmountInput(raw: string): number {
  const digits = raw.replace(/\D/g, '').slice(0, 13)
  return digits ? Math.min(Number(digits), 1_000_000_000_000) : 0
}
