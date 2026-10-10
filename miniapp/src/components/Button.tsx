import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { LoaderCircle } from 'lucide-react'

type Variant = 'primary' | 'success' | 'soft' | 'danger' | 'ghost' | 'outline'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  loading?: boolean
  icon?: ReactNode
  full?: boolean
  size?: 'md' | 'lg' | 'sm'
}

const variants: Record<Variant, string> = {
  primary: 'bg-primary text-white shadow-elevated active:bg-primary-light',
  success: 'bg-success text-white active:opacity-90',
  soft: 'bg-primary-soft text-primary active:opacity-80',
  danger: 'bg-danger-soft text-danger active:opacity-80',
  ghost: 'text-muted active:bg-field',
  outline: 'border border-line bg-card text-text active:bg-field',
}

const sizes = { sm: 'h-9 px-3 text-xs rounded-xl', md: 'h-12 px-4 text-sm rounded-2xl', lg: 'h-14 px-5 text-base rounded-2xl' }

export function Button({ variant = 'primary', loading, icon, full, size = 'md', className = '', children, disabled, ...rest }: Props) {
  return (
    <button
      type="button"
      {...rest}
      disabled={disabled || loading}
      className={`inline-flex items-center justify-center gap-2 font-bold transition disabled:opacity-50 ${sizes[size]} ${variants[variant]} ${full ? 'w-full' : ''} ${className}`}
    >
      {loading ? <LoaderCircle className="h-4 w-4 animate-spin" /> : icon}
      {children}
    </button>
  )
}

export function FAB({ label, ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { label: string }) {
  return (
    <button
      type="button"
      aria-label={label}
      {...rest}
      className="bg-primary shadow-elevated fixed right-4 z-30 flex h-14 w-14 items-center justify-center rounded-full text-white transition active:scale-95"
      style={{ bottom: 'calc(84px + var(--safe-bottom))' }}
    />
  )
}
