import { avatarColors, initials } from '../lib/people'

interface Props {
  name: string
  seed?: string
  size?: 'sm' | 'md' | 'lg'
}

const sizes = { sm: 'h-9 w-9 text-xs', md: 'h-12 w-12 text-sm', lg: 'h-14 w-14 text-base' }

/** Initials on a stable pastel background. */
export function Avatar({ name, seed, size = 'md' }: Props) {
  const { bg, fg } = avatarColors(seed ?? name)
  return (
    <div
      className={`flex shrink-0 items-center justify-center rounded-2xl font-extrabold ${sizes[size]}`}
      style={{ backgroundColor: bg, color: fg }}
      aria-hidden
    >
      {initials(name)}
    </div>
  )
}
