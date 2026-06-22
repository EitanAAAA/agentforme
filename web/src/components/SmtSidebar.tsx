import type { SmtEventDto } from '../types'

type Props = {
  events: SmtEventDto[]
  selectedId?: string | null
  onSelect: (event: SmtEventDto) => void
}

export function SmtSidebar({ events, selectedId, onSelect }: Props) {
  return (
    <aside className="smt-sidebar">
      <div className="sidebar-head">
        <h2>SMT Events</h2>
        <span>{events.length}</span>
      </div>
      <div className="event-list">
        {events.map((event) => (
          <button
            type="button"
            key={event.id}
            className={`event-card ${event.direction.toLowerCase()} ${selectedId === event.id ? 'selected' : ''}`}
            onClick={() => onSelect(event)}
          >
            <div>
              <strong>{event.setupType}</strong>
              <time>{new Date(event.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</time>
            </div>
            <span>{event.direction} SMT</span>
            <p>{event.reason}</p>
          </button>
        ))}
        {events.length === 0 && <p className="empty">Waiting for SMT events.</p>}
      </div>
    </aside>
  )
}
