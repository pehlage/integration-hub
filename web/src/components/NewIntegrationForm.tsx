import { useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api, type ErpType } from '../api'
import { Modal } from './Modal'

export function NewIntegrationForm({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [erp, setErp] = useState<ErpType>('Acme')
  const [baseUrl, setBaseUrl] = useState('http://mock-erp:8080')
  const [cron, setCron] = useState('*/15 * * * *')

  const create = useMutation({
    mutationFn: () => api.create({ name: name.trim(), erp, baseUrl: baseUrl.trim(), cron: cron.trim() || undefined }),
    onSuccess: () => {
      queryClient.invalidateQueries()
      onClose()
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    create.mutate()
  }

  return (
    <Modal title="Nova integração" onClose={onClose}>
      <form onSubmit={submit} className="form">
        <label>
          Nome
          <input value={name} onChange={(e) => setName(e.target.value)} required maxLength={120} autoFocus />
        </label>
        <label>
          ERP
          <select value={erp} onChange={(e) => setErp(e.target.value as ErpType)}>
            <option value="Acme">Acme (paginação por número)</option>
            <option value="Globex">Globex (paginação por cursor)</option>
          </select>
        </label>
        <label>
          URL base do ERP
          <input type="url" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} required />
        </label>
        <label>
          Agenda (cron)
          <input value={cron} onChange={(e) => setCron(e.target.value)} placeholder="*/15 * * * *" />
          <span className="muted small">Formato cron de 5 campos. Padrão: a cada 15 minutos.</span>
        </label>
        {create.error && (
          <p className="error" role="alert">
            {create.error.message}
          </p>
        )}
        <footer className="form-actions">
          <button type="button" className="button ghost" onClick={onClose}>
            Cancelar
          </button>
          <button type="submit" className="button primary" disabled={create.isPending}>
            Criar e ativar
          </button>
        </footer>
      </form>
    </Modal>
  )
}
