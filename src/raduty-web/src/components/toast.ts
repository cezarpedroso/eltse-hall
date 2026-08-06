import { createContext, useContext } from 'react'

export type ToastKind = 'success' | 'error'
export const ToastContext = createContext<(message: string, kind?: ToastKind) => void>(() => undefined)
export const useToast = () => useContext(ToastContext)

