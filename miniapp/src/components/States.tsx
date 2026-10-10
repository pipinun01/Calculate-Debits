import type { ReactNode } from 'react'
import { CircleAlert, LoaderCircle } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'

export function Loading({ full }: { full?: boolean }) {
  return (
    <div className={`text-primary flex items-center justify-center ${full ? 'h-full min-h-[60dvh]' : 'py-10'}`}>
      <LoaderCircle className="h-7 w-7 animate-spin" />
    </div>
  )
}

export function Empty({ icon, title, hint, action }: { icon?: ReactNode; title: string; hint?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center px-6 py-10 text-center">
      {icon && <div className="bg-primary-soft text-primary mb-3 flex h-14 w-14 items-center justify-center rounded-2xl">{icon}</div>}
      <p className="text-sm font-extrabold">{title}</p>
      {hint && <p className="text-muted mt-1 text-xs">{hint}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function ErrorState({ onRetry, message }: { onRetry?: () => void; message?: string }) {
  const { t } = useTranslation()
  return <Empty icon={<CircleAlert className="h-6 w-6" />} title={message ?? t('errors.generic')} action={onRetry && <Button variant="soft" onClick={onRetry}>{t('common.retry')}</Button>} />
}

/** Section caption: "MIJOZLAR RO'YXATI" on the left, a small note on the right. */
export function SectionTitle({ children, right }: { children: ReactNode; right?: ReactNode }) {
  return (
    <div className="mt-5 mb-2 flex items-center justify-between px-1">
      <h2 className="text-muted text-[11px] font-extrabold tracking-widest uppercase">{children}</h2>
      {right && <span className="text-muted text-[11px] font-semibold">{right}</span>}
    </div>
  )
}

export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return (
    <label className="mb-3 block">
      <span className="text-muted mb-1.5 block px-1 text-[11px] font-extrabold tracking-wider uppercase">{label}</span>
      {children}
      {hint && <span className="text-muted mt-1 block px-1 text-[11px]">{hint}</span>}
    </label>
  )
}

export const inputClass = 'bg-card border-line h-12 w-full rounded-2xl border px-3.5 text-sm outline-none focus:border-primary placeholder:text-muted'
