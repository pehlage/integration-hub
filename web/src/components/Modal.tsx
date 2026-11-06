import { useEffect, useRef, type ReactNode } from 'react'

interface Props {
  title: string
  onClose: () => void
  children: ReactNode
  wide?: boolean
}

/** Usa o <dialog> nativo: foco preso no modal, ESC fecha e o leitor de tela anuncia o título. */
export function Modal({ title, onClose, children, wide }: Props) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (dialog && !dialog.open) dialog.showModal?.()
  }, [])

  return (
    <dialog
      ref={ref}
      className={`modal${wide ? ' modal-wide' : ''}`}
      aria-label={title}
      onCancel={(e) => {
        e.preventDefault()
        onClose()
      }}
      open={typeof HTMLDialogElement !== 'undefined' && !('showModal' in HTMLDialogElement.prototype) ? true : undefined}
    >
      <header className="modal-header">
        <h2>{title}</h2>
        <button className="button ghost small" onClick={onClose} aria-label="Fechar">
          ✕
        </button>
      </header>
      {children}
    </dialog>
  )
}
