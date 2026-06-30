import { useMemo, useState } from 'react'
import type { SmtEventDto } from '../types'

const smtTypeFilters = [
  { value: 'HighLow', label: 'H/L' },
  { value: 'Fvg', label: 'FVG' },
  { value: 'InvertedFvg', label: 'IFVG' },
] as const

type SmtTypeFilter = typeof smtTypeFilters[number]['value']

type Props = {
  events: SmtEventDto[]
  selectedId?: string | null
  onSelect: (event: SmtEventDto) => void
}

export function SmtSidebar({ events, selectedId, onSelect }: Props) {
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [activeTypes, setActiveTypes] = useState<Set<SmtTypeFilter>>(() => new Set<SmtTypeFilter>(['HighLow']))
  const filteredEvents = useMemo(
    () => events.filter((event) => activeTypes.has(event.setupType as SmtTypeFilter)),
    [activeTypes, events],
  )
  const allTypesEnabled = activeTypes.size === smtTypeFilters.length

  function toggleType(type: SmtTypeFilter) {
    setActiveTypes((current) => {
      const next = new Set(current)
      if (next.has(type)) {
        next.delete(type)
      } else {
        next.add(type)
      }

      return next.size === 0 ? current : next
    })
  }

  return (
    <aside className="smt-sidebar">
      <div className="sidebar-head">
        <h2>SMT Events</h2>
        <div className="sidebar-head-actions">
          <button
            type="button"
            className={`filter-toggle ${filtersOpen ? 'active' : ''}`}
            onClick={() => setFiltersOpen((value) => !value)}
          >
            Filter
          </button>
          <span>{filteredEvents.length}/{events.length}</span>
        </div>
      </div>
      {filtersOpen && (
        <div className="smt-filter-row" aria-label="SMT type filters">
          <button type="button" className={allTypesEnabled ? 'active' : ''} onClick={() => setActiveTypes(new Set(smtTypeFilters.map((item) => item.value)))}>
            All
          </button>
          {smtTypeFilters.map((filter) => (
            <button
              key={filter.value}
              type="button"
              className={activeTypes.has(filter.value) ? 'active' : ''}
              onClick={() => toggleType(filter.value)}
            >
              {filter.label}
            </button>
          ))}
        </div>
      )}
      <div className="event-list">
        {filteredEvents.map((event) => (
          <button
            type="button"
            key={event.id}
            className={`event-card ${event.direction.toLowerCase()} ${event.status.toLowerCase()} ${selectedId === event.id ? 'selected' : ''}`}
            onClick={() => onSelect(event)}
          >
            <div>
              <strong>{event.setupType}</strong>
              <time>{new Date(event.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</time>
            </div>
            <span>{event.direction} SMT{event.status === 'Canceled' ? ' - Canceled' : ''}</span>
            <p>{event.reason}</p>
            {selectedId === event.id && <small>Click again to open NQ 1m focus</small>}
          </button>
        ))}
        {events.length === 0 && <p className="empty">Waiting for SMT events.</p>}
        {events.length > 0 && filteredEvents.length === 0 && <p className="empty">No SMT events match the filter.</p>}
      </div>
    </aside>
  )
}
