import { useEffect, type ReactNode } from 'react'
import { X } from 'lucide-react'

interface Props {
  open: boolean
  onClose(): void
  title?: ReactNode
  children: ReactNode
  footer?: ReactNode
}

/** Modal sheet: slides up from the bottom on phones, sits in the middle as a dialog on tablets. The body scrolls, the footer stays pinned. */
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
    <div className="fixed inset-0 z-50 flex items-end justify-center md:items-center md:p-6" role="dialog" aria-modal>
      <div className="animate-fade-in absolute inset-0 bg-slate-950/45 backdrop-blur-[2px]" onClick={onClose} />
      <div className="animate-sheet-in md:animate-pop-in bg-card border-line rounded-t-sheet md:rounded-sheet relative flex max-h-[92dvh] w-full max-w-xl flex-col border-t shadow-[0_-12px_40px_rgba(15,23,42,0.18)] md:max-h-[86dvh] md:border">
        <div className="bg-track mx-auto mt-2.5 h-1.5 w-10 shrink-0 rounded-full md:hidden" />
        <div className="border-line/70 flex items-center justify-between gap-3 border-b px-5 pt-3 pb-3">
          <div className="min-w-0 flex-1 text-base font-extrabold">{title}</div>
          <button type="button" onClick={onClose} aria-label="Close" className="bg-field text-muted border-line flex h-8 w-8 shrink-0 items-center justify-center rounded-full border transition active:scale-95">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="no-scrollbar flex-1 overflow-y-auto px-5 pt-4 pb-4">{children}</div>
        {footer && (
          <div className="px-5 pt-2" style={{ paddingBottom: 'calc(14px + var(--safe-bottom))' }}>
            {footer}
          </div>
        )}
      </div>
    </div>
  )
}
