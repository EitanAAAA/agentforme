import type { DataStatusDto } from '../types'

type Props = {
  status?: DataStatusDto | null
}

export function StatusBadge({ status }: Props) {
  return (
    <div className={`status-badge ${(status?.dataStatus ?? 'Delayed').toLowerCase()}`}>
      <span>{status?.message ?? 'DELAYED DATA - live updating view'}</span>
      <small>{status?.lastUpdated ? new Date(status.lastUpdated).toLocaleTimeString() : 'waiting'}</small>
    </div>
  )
}
