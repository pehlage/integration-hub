export type ErpType = 'Acme' | 'Globex'
export type RunStatus = 'Running' | 'Succeeded' | 'Failed'

export interface Run {
  id: string
  status: RunStatus
  trigger: string
  from: string
  to: string
  pages: number
  records: number
  error: string | null
  startedAt: string
  finishedAt: string | null
  durationSeconds: number | null
}

export interface Integration {
  id: string
  name: string
  erp: ErpType
  baseUrl: string
  enabled: boolean
  cron: string
  records: number
  lastRun: Run | null
}

export interface Dashboard {
  integrations: number
  enabled: number
  records: number
  runsLast24h: number
  failedLast24h: number
  avgDurationSeconds: number | null
}

export interface NewIntegration {
  name: string
  erp: ErpType
  baseUrl: string
  cron?: string
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
  })
  if (!response.ok) {
    // A API devolve ProblemDetails; mostra o "detail" quando existir.
    const problem = await response.json().catch(() => null)
    throw new ApiError(response.status, problem?.detail ?? problem?.title ?? `Erro ${response.status}`)
  }
  return response.status === 204 ? (undefined as T) : response.json()
}

const post = <T = void>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })

export const api = {
  dashboard: () => request<Dashboard>('/dashboard'),
  integrations: () => request<Integration[]>('/integrations'),
  runs: (id: string) => request<Run[]>(`/integrations/${id}/runs?take=20`),
  create: (body: NewIntegration) => post<{ id: string }>('/integrations', body),
  setEnabled: (id: string, enabled: boolean) => post(`/integrations/${id}/${enabled ? 'enable' : 'disable'}`),
  runNow: (id: string) => post<{ jobId: string }>(`/integrations/${id}/run`),
  backfill: (id: string, from: string, to: string) =>
    post<{ windows: number; windowDays: number }>(`/integrations/${id}/backfill`, { from, to }),
}
