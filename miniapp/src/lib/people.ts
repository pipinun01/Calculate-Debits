/** "Ahmad aka" → "AA", "Jasur" → "JA", "" → "?" */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return '?'
  const first = [...words[0]]
  if (words.length === 1) return (first[0] + (first[1] ?? '')).toUpperCase()
  return (first[0] + [...words[1]][0]).toUpperCase()
}

const PALETTE = [
  ['#fee2e2', '#dc2626'],
  ['#fef3c7', '#d97706'],
  ['#ede9fe', '#7c3aed'],
  ['#dbeafe', '#2563eb'],
  ['#d1fae5', '#059669'],
  ['#fce7f3', '#db2777'],
  ['#e0f2fe', '#0284c7'],
] as const

/** Stable pastel colour pair for an avatar, derived from the id / name. */
export function avatarColors(seed: string): { bg: string; fg: string } {
  let h = 0
  for (const ch of seed) h = (h * 31 + ch.codePointAt(0)!) >>> 0
  const [bg, fg] = PALETTE[h % PALETTE.length]
  return { bg, fg }
}

export const displayName = (name: string, tag?: string | null): string => (tag ? `${name} (${tag})` : name)

/** Phone as typed → "tel:" link, or null when there are too few digits to call. */
export function telLink(phone?: string | null): string | null {
  if (!phone) return null
  const cleaned = phone.replace(/[^\d+]/g, '')
  return cleaned.replace(/\D/g, '').length >= 7 ? `tel:${cleaned}` : null
}
