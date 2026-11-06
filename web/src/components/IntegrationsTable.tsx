import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api, type Integration } from '../api'
import { formatNumber, formatRelative } from '../format'
import { StatusBadge } from './StatusBadge'
import { BackfillDialog } from './BackfillDialog'

interface Props {
  items: Integration[]
  selectedId: string | null
  onSelect: (id: string) => void
}

export function IntegrationsTable({ items, selectedId, onSelect }: Props) {
  const queryClient = useQueryClient()
  const [backfillFor, setBackfillFor] = useState<Integration | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const refresh = () => queryClient.invalidateQueries()

  const toggle = useMutation({
    mutationFn: (i: Integration) => api.setEnabled(i.id, !i.enabled),
    onSuccess: refresh,
  })

  const runNow = useMutation({
    mutationFn: (i: Integration) => api.runNow(i.id),
    onSuccess: (_, i) => {
      setNotice(`Execução de "${i.name}" enfileirada.`)
      refresh()
    },
  })

  if (items.length === 0) return <p className="muted">Nenhuma integração cadastrada.</p>

  const error = toggle.error ?? runNow.error

  return (
    <>
      {notice && (
        <p className="notice" role="status">
          {notice}
        </p>
      )}
      {error && (
        <p className="error" role="alert">
          {error.message}
        </p>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Integração</th>
              <th>ERP</th>
              <th>Agenda</th>
              <th>Última execução</th>
              <th className="num">Vendas</th>
              <th aria-label="Ações" />
            </tr>
          </thead>
          <tbody>
            {items.map((i) => (
              <tr key={i.id} className={i.id === selectedId ? 'selected' : undefined}>
                <td>
                  <button className="link" onClick={() => onSelect(i.id)}>
                    {i.name}
                  </button>
                  {!i.enabled && <span className="badge badge-idle">Desativada</span>}
                </td>
                <td>{i.erp}</td>
                <td>
                  <code>{i.cron}</code>
                </td>
                <td>
                  <StatusBadge status={i.lastRun?.status ?? null} />
                  {i.lastRun && <span className="muted small"> {formatRelative(i.lastRun.startedAt)}</span>}
                </td>
                <td className="num">{formatNumber(i.records)}</td>
                <td className="actions">
                  <button
                    className="button small"
                    disabled={!i.enabled || runNow.isPending}
                    onClick={() => runNow.mutate(i)}
                  >
                    Rodar agora
                  </button>
                  <button className="button small" disabled={!i.enabled} onClick={() => setBackfillFor(i)}>
                    Backfill
                  </button>
                  <button
                    className={`button small ${i.enabled ? 'ghost' : 'primary'}`}
                    disabled={toggle.isPending}
                    onClick={() => toggle.mutate(i)}
                  >
                    {i.enabled ? 'Desativar' : 'Ativar integração'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {backfillFor && (
        <BackfillDialog
          integration={backfillFor}
          onClose={() => setBackfillFor(null)}
          onQueued={(message) => {
            setNotice(message)
            setBackfillFor(null)
            refresh()
          }}
        />
      )}
    </>
  )
}
