import type { RunStatus } from '../api'

const labels: Record<RunStatus, string> = {
  Running: 'Executando',
  Succeeded: 'Sucesso',
  Failed: 'Falhou',
}

export function StatusBadge({ status }: { status: RunStatus | null }) {
  if (!status) return <span className="badge badge-idle">Sem execuções</span>
  return <span className={`badge badge-${status.toLowerCase()}`}>{labels[status]}</span>
}
