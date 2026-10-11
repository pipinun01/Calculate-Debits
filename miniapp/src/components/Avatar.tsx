import type { CSSProperties } from 'react'
import { avatarColors, initials } from '../lib/people'

interface Props {
  name: string
  seed?: string
  size?: 'sm' | 'md' | 'lg'
  round?: boolean
}

const sizes = { sm: 'h-9 w-9 text-xs', md: 'h-11 w-11 text-sm', lg: 'h-14 w-14 text-base' }

/** Initials on a stable tinted background; the tint is derived from one colour, so it works in both themes. */
export function Avatar({ name, seed, size = 'md', round }: Props) {
  const { fg } = avatarColors(seed ?? name)
  return (
    <div
      className={`flex shrink-0 items-center justify-center border border-[color-mix(in_srgb,var(--av)_26%,transparent)] bg-[color-mix(in_srgb,var(--av)_12%,transparent)] font-bold text-[var(--av)] dark:text-[color-mix(in_srgb,var(--av)_55%,white)] ${round ? 'rounded-full' : 'rounded-2xl'} ${sizes[size]}`}
      style={{ '--av': fg } as CSSProperties}
      aria-hidden
    >
      {initials(name)}
    </div>
  )
}
