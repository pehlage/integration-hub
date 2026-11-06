import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import { SummaryCards } from './components/SummaryCards'
import { IntegrationsTable } from './components/IntegrationsTable'
import { RunHistory } from './components/RunHistory'
import { NewIntegrationForm } from './components/NewIntegrationForm'

export function App() {
  const integrations = useQuery({ queryKey: ['integrations'], queryFn: api.integrations })
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const selected = integrations.data?.find((i) => i.id === selectedId) ?? null

  return (
    <div className="page">
      <header className="topbar">
        <div>
          <h1>Integration Hub</h1>
          <p className="muted">Sincronização de vendas com ERPs parceiros</p>
        </div>
        <nav className="topbar-actions">
          <a className="button ghost" href="/hangfire" target="_blank" rel="noreferrer">
            Painel Hangfire
          </a>
          <button className="button primary" onClick={() => setCreating(true)}>
            Nova integração
          </button>
        </nav>
      </header>

      <SummaryCards />

      <section className="panel">
        <h2>Integrações</h2>
        {integrations.isPending && <p className="muted">Carregando…</p>}
        {integrations.isError && (
          <p className="error" role="alert">
            Não foi possível carregar as integrações: {integrations.error.message}
          </p>
        )}
        {integrations.data && (
          <IntegrationsTable items={integrations.data} selectedId={selectedId} onSelect={setSelectedId} />
        )}
      </section>

      {selected && <RunHistory integration={selected} onClose={() => setSelectedId(null)} />}
      {creating && <NewIntegrationForm onClose={() => setCreating(false)} />}
    </div>
  )
}
