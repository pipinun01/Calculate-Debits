import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { haptic } from '../lib/telegram'

type Kind = 'info' | 'success' | 'error'

interface ToastApi {
  show(message: string, kind?: Kind): void
  /** Shows the translated text for a backend error code (errors.<code>), or a generic message. */
  error(error: unknown): void
}

const ToastContext = createContext<ToastApi | null>(null)

export function useToast(): ToastApi {
  const toast = useContext(ToastContext)
  if (!toast) throw new Error('useToast outside ToastProvider')
  return toast
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  const [toast, setToast] = useState<{ message: string; kind: Kind; id: number } | null>(null)
  const timer = useRef<number | undefined>(undefined)

  const show = useCallback((message: string, kind: Kind = 'info') => {
    window.clearTimeout(timer.current)
    setToast({ message, kind, id: Date.now() })
    haptic(kind === 'error' ? 'error' : kind === 'success' ? 'success' : 'tap')
    timer.current = window.setTimeout(() => setToast(null), kind === 'error' ? 4000 : 2500)
  }, [])

  const error = useCallback(
    (e: unknown) => {
      const code = e instanceof ApiError ? e.code : 'network'
      show(t(`errors.${code}`, { defaultValue: t('errors.generic') }), 'error')
    },
    [show, t]
  )

  const colors = { info: 'bg-dark-card text-white dark:border dark:border-line', success: 'bg-success-fill text-white', error: 'bg-danger-fill text-white' }

  return (
    <ToastContext.Provider value={{ show, error }}>
      {children}
      {toast && (
        <div className="pointer-events-none fixed inset-x-0 top-3 z-[60] flex justify-center px-4" role="status">
          <div key={toast.id} className={`animate-fade-in shadow-card max-w-sm rounded-2xl px-4 py-2.5 text-sm font-semibold ${colors[toast.kind]}`}>
            {toast.message}
          </div>
        </div>
      )}
    </ToastContext.Provider>
  )
}
