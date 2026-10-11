import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { LoaderCircle } from 'lucide-react'

type Variant = 'primary' | 'success' | 'soft' | 'danger' | 'ghost' | 'outline' | 'telegram'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  loading?: boolean
  icon?: ReactNode
  full?: boolean
  size?: 'md' | 'lg' | 'sm'
}

const variants: Record<Variant, string> = {
  primary: 'bg-primary text-white shadow-elevated active:bg-primary-light dark:border dark:border-blue-400/30',
  success: 'bg-success-fill text-white shadow-[0_8px_20px_-6px_rgba(5,150,105,0.5)] dark:border dark:border-emerald-400/30',
  soft: 'bg-primary-tint text-accent border border-primary-line',
  danger: 'bg-danger-soft text-danger border border-danger-line',
  ghost: 'text-muted active:bg-field',
  outline: 'border border-line bg-card text-text shadow-soft active:bg-field',
  telegram: 'bg-telegram text-white shadow-[0_8px_20px_-4px_rgba(36,161,222,0.4)]',
}

const sizes = { sm: 'h-9 px-3 text-xs rounded-xl', md: 'h-12 px-4 text-sm rounded-xl', lg: 'h-14 px-5 text-base rounded-2xl' }

export function Button({ variant = 'primary', loading, icon, full, size = 'md', className = '', children, disabled, ...rest }: Props) {
  return (
    <button
      type="button"
      {...rest}
      disabled={disabled || loading}
      className={`inline-flex min-w-0 items-center justify-center gap-2 font-bold transition active:scale-[0.98] disabled:opacity-50 ${sizes[size]} ${variants[variant]} ${full ? 'w-full' : ''} ${className}`}
    >
      {loading ? <LoaderCircle className="h-4 w-4 shrink-0 animate-spin" /> : icon}
      {children}
    </button>
  )
}

/** Round icon button of the top bars. */
export function IconButton({ className = '', ...rest }: ButtonHTMLAttributes<HTMLButtonElement>) {
  return <button type="button" {...rest} className={`bg-card border-line text-accent shadow-soft flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border transition active:scale-95 disabled:opacity-50 ${className}`} />
}

/** Floating action button, kept inside the content column on wide screens. */
export function FAB({ label, ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { label: string }) {
  return (
    <div className="pointer-events-none fixed inset-x-0 z-30" style={{ bottom: 'calc(84px + var(--safe-bottom))' }}>
      <div className="app-frame app-x flex justify-end">
        <button
          type="button"
          aria-label={label}
          {...rest}
          className="bg-chip text-on-chip shadow-elevated ring-chip/20 pointer-events-auto flex h-14 w-14 items-center justify-center rounded-full ring-4 transition active:scale-90"
        />
      </div>
    </div>
  )
}
