import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { formatDuration, formatNumber } from '../format'

export function SummaryCards() {
  const { data } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard })

  const cards = [
    { label: 'Integrações ativas', value: data ? `${data.enabled} de ${data.integrations}` : '—' },
    { label: 'Vendas sincronizadas', value: data ? formatNumber(data.records) : '—' },
    { label: 'Execuções (24 h)', value: data ? formatNumber(data.runsLast24h) : '—' },
    {
      label: 'Falhas (24 h)',
      value: data ? formatNumber(data.failedLast24h) : '—',
      alert: (data?.failedLast24h ?? 0) > 0,
    },
    { label: 'Duração média', value: data ? formatDuration(data.avgDurationSeconds) : '—' },
  ]

  return (
    <section className="cards" aria-label="Resumo">
      {cards.map((card) => (
        <div key={card.label} className={`card${card.alert ? ' card-alert' : ''}`}>
          <span className="card-label">{card.label}</span>
          <strong className="card-value">{card.value}</strong>
        </div>
      ))}
    </section>
  )
}
