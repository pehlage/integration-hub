import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { App } from './App'
import type { Dashboard, Integration } from './api'

const dashboard: Dashboard = {
  integrations: 2,
  enabled: 1,
  records: 12345,
  runsLast24h: 40,
  failedLast24h: 2,
  avgDurationSeconds: 3.2,
}

const integrations: Integration[] = [
  {
    id: 'a1',
    name: 'Rede Acme',
    erp: 'Acme',
    baseUrl: 'http://mock',
    enabled: true,
    cron: '*/15 * * * *',
    records: 12000,
    lastRun: {
      id: 'r1', status: 'Succeeded', trigger: 'schedule', from: '2026-11-05', to: '2026-11-06', pages: 3,
      records: 420, error: null, startedAt: new Date().toISOString(), finishedAt: new Date().toISOString(), durationSeconds: 2,
    },
  },
  {
    id: 'g1', name: 'Globex', erp: 'Globex', baseUrl: 'http://mock', enabled: false, cron: '0 * * * *', records: 345, lastRun: null,
  },
]

/** Fetch falso que responde às rotas da API e registra as chamadas de escrita. */
function mockApi(overrides: Record<string, () => Response> = {}) {
  const calls: { method: string; url: string; body?: unknown }[] = []
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = String(input)
    const method = init?.method ?? 'GET'
    calls.push({ method, url, body: init?.body ? JSON.parse(String(init.body)) : undefined })
    const key = `${method} ${url.replace(/\?.*/, '')}`
    if (overrides[key]) return overrides[key]()
    if (key === 'GET /api/dashboard') return Response.json(dashboard)
    if (key === 'GET /api/integrations') return Response.json(integrations)
    if (key.endsWith('/runs')) return Response.json([integrations[0].lastRun])
    if (key.endsWith('/run')) return Response.json({ jobId: '1' }, { status: 202 })
    if (key.endsWith('/backfill')) return Response.json({ windows: 5, windowDays: 7 }, { status: 202 })
    return new Response(null, { status: 204 })
  })
  return calls
}

function renderApp() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <App />
    </QueryClientProvider>,
  )
}

describe('Dashboard', () => {
  it('mostra o resumo e as integrações', async () => {
    mockApi()
    renderApp()

    expect(await screen.findByText('Rede Acme')).toBeInTheDocument()
    expect(screen.getByText('1 de 2')).toBeInTheDocument()
    expect(screen.getByText('12.345')).toBeInTheDocument()
    expect(screen.getByText('Sucesso')).toBeInTheDocument()
    expect(screen.getByText('Sem execuções')).toBeInTheDocument()
  })

  it('ativa uma integração desativada', async () => {
    const calls = mockApi()
    renderApp()

    await userEvent.click(await screen.findByRole('button', { name: 'Ativar integração' }))

    await waitFor(() => expect(calls).toContainEqual(expect.objectContaining({ method: 'POST', url: '/api/integrations/g1/enable' })))
  })

  it('enfileira execução manual e avisa o usuário', async () => {
    const calls = mockApi()
    renderApp()

    const row = (await screen.findByText('Rede Acme')).closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Rodar agora' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Execução de "Rede Acme" enfileirada.')
    expect(calls).toContainEqual(expect.objectContaining({ method: 'POST', url: '/api/integrations/a1/run' }))
  })

  it('envia o backfill com o período escolhido', async () => {
    const calls = mockApi()
    renderApp()

    const row = (await screen.findByText('Rede Acme')).closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Backfill' }))
    await userEvent.click(screen.getByRole('button', { name: 'Enfileirar backfill' }))

    expect(await screen.findByRole('status')).toHaveTextContent('5 janela(s) de 7 dias')
    const call = calls.find((c) => c.url.endsWith('/backfill'))
    expect(call?.body).toEqual({ from: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/), to: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) })
  })

  it('mostra a mensagem de validação da API ao criar integração inválida', async () => {
    mockApi({
      'POST /api/integrations': () =>
        Response.json({ title: 'Validation', detail: 'Cron inválido.' }, { status: 400 }),
    })
    renderApp()

    await userEvent.click(await screen.findByRole('button', { name: 'Nova integração' }))
    await userEvent.type(screen.getByLabelText('Nome'), 'Teste')
    await userEvent.click(screen.getByRole('button', { name: 'Criar e ativar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Cron inválido.')
  })

  it('avisa quando a API está fora do ar', async () => {
    mockApi({ 'GET /api/integrations': () => new Response(null, { status: 503 }) })
    renderApp()

    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível carregar as integrações')
  })
})
