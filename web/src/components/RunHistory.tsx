import { useQuery } from '@tanstack/react-query'
import { api, type Integration } from '../api'
import { formatDateTime, formatDay, formatDuration, formatNumber } from '../format'
import { StatusBadge } from './StatusBadge'
import { Modal } from './Modal'

const triggers: Record<string, string> = {
  recurring: 'Agendada',
  manual: 'Manual',
  backfill: 'Backfill',
}

export function RunHistory({ integration, onClose }: { integration: Integration; onClose: () => void }) {
  const runs = useQuery({ queryKey: ['runs', integration.id], queryFn: () => api.runs(integration.id) })

  return (
    <Modal title={`Histórico — ${integration.name}`} onClose={onClose} wide>
      {runs.isPending && <p className="muted">Carregando…</p>}
      {runs.isError && <p className="error">{runs.error.message}</p>}
      {runs.data?.length === 0 && <p className="muted">Nenhuma execução ainda.</p>}
      {!!runs.data?.length && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Início</th>
                <th>Origem</th>
                <th>Período</th>
                <th>Status</th>
                <th className="num">Páginas</th>
                <th className="num">Vendas</th>
                <th className="num">Duração</th>
              </tr>
            </thead>
            <tbody>
              {runs.data.map((r) => (
                <tr key={r.id}>
                  <td>{formatDateTime(r.startedAt)}</td>
                  <td>{triggers[r.trigger] ?? r.trigger}</td>
                  <td>
                    {formatDay(r.from)}
                    {r.to !== r.from && ` – ${formatDay(r.to)}`}
                  </td>
                  <td>
                    <StatusBadge status={r.status} />
                    {r.error && <div className="error small" title={r.error}>{r.error}</div>}
                  </td>
                  <td className="num">{r.pages}</td>
                  <td className="num">{formatNumber(r.records)}</td>
                  <td className="num">{formatDuration(r.durationSeconds)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Modal>
  )
}
