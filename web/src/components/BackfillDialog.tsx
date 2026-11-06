import { useState, type FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api, type Integration } from '../api'
import { Modal } from './Modal'

const isoDay = (date: Date) => date.toISOString().slice(0, 10)

interface Props {
  integration: Integration
  onClose: () => void
  onQueued: (message: string) => void
}

export function BackfillDialog({ integration, onClose, onQueued }: Props) {
  const today = isoDay(new Date())
  const [from, setFrom] = useState(isoDay(new Date(Date.now() - 30 * 86400000)))
  const [to, setTo] = useState(today)

  const backfill = useMutation({
    mutationFn: () => api.backfill(integration.id, from, to),
    onSuccess: (r) => onQueued(`Backfill de "${integration.name}" enfileirado em ${r.windows} janela(s) de ${r.windowDays} dias.`),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    backfill.mutate()
  }

  return (
    <Modal title={`Backfill — ${integration.name}`} onClose={onClose}>
      <form onSubmit={submit} className="form">
        <p className="muted">
          Reprocessa um período histórico. O intervalo é dividido em janelas de 7 dias, cada uma como um job
          independente: se uma falhar, só ela é reexecutada. Máximo de 3 anos.
        </p>
        <div className="form-row">
          <label>
            De
            <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} required />
          </label>
          <label>
            Até
            <input type="date" value={to} min={from} max={today} onChange={(e) => setTo(e.target.value)} required />
          </label>
        </div>
        {backfill.error && (
          <p className="error" role="alert">
            {backfill.error.message}
          </p>
        )}
        <footer className="form-actions">
          <button type="button" className="button ghost" onClick={onClose}>
            Cancelar
          </button>
          <button type="submit" className="button primary" disabled={backfill.isPending}>
            Enfileirar backfill
          </button>
        </footer>
      </form>
    </Modal>
  )
}
