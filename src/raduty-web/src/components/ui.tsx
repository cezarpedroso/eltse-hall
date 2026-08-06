import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { AlertTriangle, CheckCircle2, X } from 'lucide-react'
import { ToastContext, type ToastKind } from './toast'

type Toast = { id: number; message: string; kind: ToastKind }

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  function add(message: string, kind: Toast['kind'] = 'success') {
    const id = Date.now()
    setToasts((items) => [...items, { id, message, kind }])
    window.setTimeout(() => setToasts((items) => items.filter((item) => item.id !== id)), 4500)
  }
  return <ToastContext.Provider value={add}>
    {children}
    <div className="toast-region" aria-live="polite" aria-atomic="true">
      {toasts.map((toast) => <div className={`toast toast--${toast.kind}`} key={toast.id}>
        {toast.kind === 'success' ? <CheckCircle2 size={18} /> : <AlertTriangle size={18} />}
        <span>{toast.message}</span>
        <button className="icon-button" onClick={() => setToasts((items) => items.filter((item) => item.id !== toast.id))} aria-label="Dismiss notification" title="Dismiss"><X size={16} /></button>
      </div>)}
    </div>
  </ToastContext.Provider>
}

export function Dialog({ open, onClose, title, children, className = '' }: { open: boolean; onClose: () => void; title: string; children: ReactNode; className?: string }) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])
  return <dialog ref={ref} className={`dialog ${className}`} onClose={onClose} aria-labelledby={titleId}>
    <div className="dialog__header"><h2 id={titleId}>{title}</h2><button type="button" className="icon-button" onClick={onClose} aria-label="Close dialog" title="Close"><X /></button></div>
    {children}
  </dialog>
}

export function ErrorState({ title = 'Something went wrong', message, onRetry }: { title?: string; message: string; onRetry?: () => void }) {
  return <div className="state-message state-message--error" role="alert"><AlertTriangle /><div><h2>{title}</h2><p>{message}</p>{onRetry && <button className="text-button" onClick={onRetry}>Try again</button>}</div></div>
}

export function EmptyState({ title, message }: { title: string; message: string }) {
  return <div className="empty-state"><span aria-hidden="true">—</span><h3>{title}</h3><p>{message}</p></div>
}

export function CalendarSkeleton() {
  return <div className="calendar-skeleton" aria-label="Loading schedule"><div className="skeleton skeleton--title" />
    <div className="skeleton-grid">{Array.from({ length: 21 }, (_, i) => <div className="skeleton skeleton--cell" key={i} />)}</div></div>
}
