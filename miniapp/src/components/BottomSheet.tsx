import { useEffect, type ReactNode } from 'react'
import { X } from 'lucide-react'

interface Props {
  open: boolean
  onClose(): void
  title?: ReactNode
  children: ReactNode
  footer?: ReactNode
}

/** Modal sheet sliding up from the bottom; the body scrolls, the footer stays pinned. */
export function BottomSheet({ open, onClose, title, children, footer }: Props) {
  useEffect(() => {
    if (!open) return
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = previous
    }
  }, [open])

  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center" role="dialog" aria-modal>
      <div className="animate-fade-in absolute inset-0 bg-black/45" onClick={onClose} />
      <div className="animate-sheet-in bg-bg rounded-t-sheet relative flex max-h-[92dvh] w-full max-w-lg flex-col">
        <div className="flex items-center justify-between gap-3 px-4 pt-4 pb-2">
          <div className="min-w-0 flex-1 text-base font-extrabold">{title}</div>
          <button type="button" onClick={onClose} aria-label="Close" className="bg-field text-muted flex h-9 w-9 shrink-0 items-center justify-center rounded-full">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="no-scrollbar flex-1 overflow-y-auto px-4 pb-4">{children}</div>
        {footer && (
          <div className="border-line bg-bg border-t px-4 pt-3" style={{ paddingBottom: 'calc(12px + var(--safe-bottom))' }}>
            {footer}
          </div>
        )}
      </div>
    </div>
  )
}
