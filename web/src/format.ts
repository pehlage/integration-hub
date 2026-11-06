const number = new Intl.NumberFormat('pt-BR')
const dateTime = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' })

export const formatNumber = (value: number) => number.format(value)

export const formatDateTime = (iso: string) => dateTime.format(new Date(iso))

export const formatDay = (isoDate: string) => {
  const [y, m, d] = isoDate.split('-')
  return `${d}/${m}/${y}`
}

export const formatDuration = (seconds: number | null) => {
  if (seconds == null) return '—'
  if (seconds < 60) return `${seconds.toFixed(1)}s`
  return `${Math.floor(seconds / 60)}min ${Math.round(seconds % 60)}s`
}

export function formatRelative(iso: string, now = Date.now()) {
  const minutes = Math.round((now - new Date(iso).getTime()) / 60000)
  if (minutes < 1) return 'agora'
  if (minutes < 60) return `há ${minutes} min`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `há ${hours} h`
  return `há ${Math.round(hours / 24)} d`
}
