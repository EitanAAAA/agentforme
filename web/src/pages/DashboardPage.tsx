import { useEffect, useMemo, useState } from 'react'
import { api, createMarketConnection } from '../api'
import { ChartPanel, type ChartCrosshairSync, type ChartFocusRange } from '../components/ChartPanel'
import { SettingsDrawer } from '../components/SettingsDrawer'
import { SmtSidebar } from '../components/SmtSidebar'
import { StatusBadge } from '../components/StatusBadge'
import type {
  AppSettingsDto,
  CandleDto,
  ChartAnnotationDto,
  DataStatusDto,
  NqOneMinuteAnalysisDto,
  SmtEventCanceledDto,
  SmtEventDto,
} from '../types'
import { FocusedNqAnalysisPage } from './FocusedNqAnalysisPage'

const defaultToggles = {
  bos: true,
  fvg: true,
  ifvg: true,
  halfBox: true,
  slTp: true,
}

type ToastState = {
  message: string
  tone: 'info' | 'danger'
}

function buildSelectedSmtAnnotations(event: SmtEventDto, symbol: 'ES' | 'NQ'): ChartAnnotationDto[] {
  const isEs = symbol === 'ES'
  const previousSwingTimestamp = isEs ? event.esPreviousSwingTimestamp : event.nqPreviousSwingTimestamp
  const previousSwingValue = isEs ? event.esPreviousSwingValue : event.nqPreviousSwingValue
  const currentValue = isEs ? event.esCurrentValue : event.nqCurrentValue
  const currentTimestamp = isEs ? event.esCurrentTimestamp : event.nqCurrentTimestamp
  const fvgLower = isEs ? event.esFvgLower : event.nqFvgLower
  const fvgUpper = isEs ? event.esFvgUpper : event.nqFvgUpper
  const fvgStart = isEs ? event.esFvgStartTimestamp : event.nqFvgStartTimestamp
  const side = event.direction === 'Bearish' ? 'high' : 'low'
  const role = event.leaderSymbol === symbol ? `broke ${side}` : `failed ${side}`
  const pointLabel = event.leaderSymbol === symbol ? `SMT point ${side}` : `Held point ${side}`

  if (event.setupType === 'HighLow' && previousSwingTimestamp) {
    if (!sameTimestamp(event.esCurrentTimestamp, event.nqCurrentTimestamp)) {
      return []
    }

    return [{
      kind: 'Smt',
      direction: event.direction,
      startTimestamp: previousSwingTimestamp,
      endTimestamp: currentTimestamp,
      price: previousSwingValue,
      secondaryPrice: currentValue,
      tertiaryPrice: null,
      label: `${symbol} ${role}`,
      isTriggered: true,
    }, {
      kind: 'SmtExtreme',
      direction: event.direction,
      startTimestamp: currentTimestamp,
      endTimestamp: currentTimestamp,
      price: currentValue,
      secondaryPrice: null,
      tertiaryPrice: null,
      label: pointLabel,
      isTriggered: true,
    }]
  }

  if ((event.setupType === 'Fvg' || event.setupType === 'InvertedFvg') &&
    fvgStart &&
    fvgLower !== null &&
    fvgLower !== undefined &&
    fvgUpper !== null &&
    fvgUpper !== undefined) {
    return [{
      kind: event.setupType === 'InvertedFvg' ? 'Ifvg' : 'Fvg',
      direction: event.direction,
      startTimestamp: fvgStart,
      endTimestamp: event.timestamp,
      price: fvgUpper,
      secondaryPrice: fvgLower,
      tertiaryPrice: null,
      label: `${symbol} selected SMT`,
      isTriggered: true,
    }]
  }

  return [{
    kind: 'Smt',
    direction: event.direction,
    startTimestamp: event.timestamp,
    endTimestamp: event.timestamp,
    price: currentValue,
    secondaryPrice: currentValue,
    tertiaryPrice: null,
    label: `${symbol} selected SMT`,
    isTriggered: true,
  }]
}

function sameTimestamp(first?: string | null, second?: string | null) {
  if (!first || !second) {
    return false
  }

  return new Date(first).getTime() === new Date(second).getTime()
}

function buildSelectedSmtFocusRange(event: SmtEventDto): ChartFocusRange {
  const candidates = [event.timestamp]
  if (event.setupType === 'HighLow') {
    if (event.esPreviousSwingTimestamp) candidates.push(event.esPreviousSwingTimestamp)
    if (event.nqPreviousSwingTimestamp) candidates.push(event.nqPreviousSwingTimestamp)
    if (event.esCurrentTimestamp) candidates.push(event.esCurrentTimestamp)
    if (event.nqCurrentTimestamp) candidates.push(event.nqCurrentTimestamp)
  } else {
    if (event.esFvgStartTimestamp) candidates.push(event.esFvgStartTimestamp)
    if (event.nqFvgStartTimestamp) candidates.push(event.nqFvgStartTimestamp)
  }

  const sorted = candidates
    .map((timestamp) => new Date(timestamp))
    .filter((date) => !Number.isNaN(date.getTime()))
    .sort((a, b) => a.getTime() - b.getTime())
  return {
    start: (sorted[0] ?? new Date(event.timestamp)).toISOString(),
    end: (sorted.at(-1) ?? new Date(event.timestamp)).toISOString(),
  }
}

export function DashboardPage() {
  const [status, setStatus] = useState<DataStatusDto | null>(null)
  const [settings, setSettings] = useState<AppSettingsDto | null>(null)
  const [esCandles, setEsCandles] = useState<CandleDto[]>([])
  const [nqCandles, setNqCandles] = useState<CandleDto[]>([])
  const [events, setEvents] = useState<SmtEventDto[]>([])
  const [highlightedEvent, setHighlightedEvent] = useState<SmtEventDto | null>(null)
  const [focusedEvent, setFocusedEvent] = useState<SmtEventDto | null>(null)
  const [focusedAnalysis, setFocusedAnalysis] = useState<NqOneMinuteAnalysisDto | null>(null)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [toggles, setToggles] = useState(defaultToggles)
  const [visiblePanels, setVisiblePanels] = useState({ nq: true, es: true })
  const [timeframe, setTimeframe] = useState('15m')
  const [syncedCrosshair, setSyncedCrosshair] = useState<ChartCrosshairSync | null>(null)
  const [toast, setToast] = useState<ToastState | null>(null)
  const visibleCount = Number(visiblePanels.nq) + Number(visiblePanels.es)

  useEffect(() => {
    let disposed = false
    async function load() {
      const [nextStatus, nextSettings, smtEvents] = await Promise.all([
        api.getStatus(),
        api.getSettings(),
        api.getSmtEvents(),
      ])
      if (disposed) return
      setStatus(nextStatus)
      setSettings(nextSettings)
      setTimeframe(nextSettings.timeframe || '15m')
      setEvents(smtEvents)
    }

    load().catch((error) => {
      setStatus({
        status: 'Error',
        dataStatus: 'Error',
        dataProvider: 'YahooFinance',
        lastUpdated: null,
        message: `API unavailable: ${error.message}`,
      })
    })
    return () => {
      disposed = true
    }
  }, [])

  useEffect(() => {
    let disposed = false
    async function loadCandles() {
      const [es, nq] = await Promise.all([
        api.getCandles('ES', timeframe),
        api.getCandles('NQ', timeframe),
      ])
      if (disposed) return
      setEsCandles(es)
      setNqCandles(nq)
      setSyncedCrosshair(null)
    }

    loadCandles().catch((error) => {
      setStatus({
        status: 'Error',
        dataStatus: 'Error',
        dataProvider: 'YahooFinance',
        lastUpdated: null,
        message: `Candles unavailable: ${error.message}`,
      })
    })

    return () => {
      disposed = true
    }
  }, [timeframe])

  useEffect(() => {
    const connection = createMarketConnection()
    connection.on('DataStatusChanged', setStatus)
    connection.on('CandlesUpdated', (payload: { symbol: 'ES' | 'NQ'; timeframe?: string; candles: CandleDto[] }) => {
      if (payload.timeframe && payload.timeframe !== timeframe) {
        return
      }
      if (payload.symbol === 'ES') {
        setEsCandles(payload.candles)
      } else {
        setNqCandles(payload.candles)
      }
    })
    connection.on('SmtEventUpdated', (event: SmtEventDto) => {
      setEvents((current) => [event, ...current.filter((item) => item.id !== event.id)])
    })
    connection.on('SmtEventsUpdated', (nextEvents: SmtEventDto[]) => {
      setEvents(nextEvents)
      setHighlightedEvent((current) => {
        if (!current) return current
        return nextEvents.find((event) => event.id === current.id) ?? null
      })
      setFocusedEvent((current) => {
        if (!current) return current
        const updated = nextEvents.find((event) => event.id === current.id)
        if (updated) return updated
        setFocusedAnalysis(null)
        setToast({ message: 'SMT canceled. The failed market reached the SMT level.', tone: 'danger' })
        return null
      })
    })
    connection.on('SmtEventCanceled', (canceled: SmtEventCanceledDto) => {
      setToast({ message: canceled.reason, tone: 'danger' })
      setHighlightedEvent((current) => current?.id === canceled.id ? null : current)
      setFocusedEvent((current) => {
        if (current?.id !== canceled.id) return current
        setFocusedAnalysis(null)
        return null
      })
    })
    connection.on('FocusedAnalysisUpdated', (analysis: NqOneMinuteAnalysisDto) => {
      setFocusedAnalysis((current) => current?.smtEventId === analysis.smtEventId ? analysis : current)
    })
    connection.start().catch(() => undefined)
    return () => {
      connection.stop().catch(() => undefined)
    }
  }, [timeframe])

  useEffect(() => {
    if (!focusedEvent) {
      return
    }

    const focusedEventId = focusedEvent.id
    let disposed = false
    async function refreshFocusedAnalysis() {
      try {
        const analysis = await api.getFocusedAnalysis(focusedEventId)
        if (!disposed) {
          setFocusedAnalysis(analysis)
        }
      } catch {
        // Keep the current focused replay available if a refresh misses.
      }
    }

    refreshFocusedAnalysis()
    const interval = window.setInterval(refreshFocusedAnalysis, 30_000)
    return () => {
      disposed = true
      window.clearInterval(interval)
    }
  }, [focusedEvent])

  const sortedEvents = useMemo(
    () => [...events].sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime()),
    [events],
  )

  async function selectEvent(event: SmtEventDto) {
    if (!focusedEvent && highlightedEvent?.id !== event.id) {
      setHighlightedEvent(event)
      setFocusedAnalysis(null)
      return
    }

    setHighlightedEvent(event)
    setFocusedEvent(event)
    setFocusedAnalysis(null)
    const analysis = await api.getFocusedAnalysis(event.id)
    setFocusedAnalysis(analysis)
  }

  async function updateSettings(next: AppSettingsDto) {
    setSettings(next)
    const saved = await api.updateSettings(next)
    setSettings(saved)
  }

  const esSelectedAnnotations = useMemo(
    () => highlightedEvent ? buildSelectedSmtAnnotations(highlightedEvent, 'ES') : [],
    [highlightedEvent],
  )
  const nqSelectedAnnotations = useMemo(
    () => highlightedEvent ? buildSelectedSmtAnnotations(highlightedEvent, 'NQ') : [],
    [highlightedEvent],
  )
  const selectedFocusTime = highlightedEvent?.timestamp ?? null
  const selectedFocusRange = useMemo(
    () => highlightedEvent ? buildSelectedSmtFocusRange(highlightedEvent) : null,
    [highlightedEvent],
  )

  if (focusedEvent) {
    return (
      <>
        <div className="app-shell">
          <FocusedNqAnalysisPage
            event={focusedEvent}
            analysis={focusedAnalysis}
            show={toggles}
            onToggle={(key) => setToggles((current) => ({ ...current, [key]: !current[key] }))}
            onBack={() => {
              setFocusedEvent(null)
              setFocusedAnalysis(null)
            }}
          />
          <SmtSidebar events={sortedEvents} selectedId={focusedEvent.id} onSelect={selectEvent} />
        </div>
        {toast && (
          <div className={`smt-toast ${toast.tone}`} role="status">
            <span>{toast.message}</span>
            <button type="button" onClick={() => setToast(null)}>Close</button>
          </div>
        )}
      </>
    )
  }

  return (
    <div className="app-shell">
      <main className="dashboard-page">
        <header className="topbar">
          <div>
            <h1>SMT Agent</h1>
            <p>Local delayed-data strategy dashboard</p>
          </div>
          <div className="topbar-actions">
            <StatusBadge status={status} />
            <button type="button" onClick={() => setSettingsOpen(true)}>Settings</button>
          </div>
        </header>
        {visibleCount < 2 && (
          <div className="restore-strip">
            {!visiblePanels.nq && <button type="button" onClick={() => setVisiblePanels((current) => ({ ...current, nq: true }))}>Restore NQ</button>}
            {!visiblePanels.es && <button type="button" onClick={() => setVisiblePanels((current) => ({ ...current, es: true }))}>Restore ES</button>}
          </div>
        )}
        <div className={`chart-stack ${visibleCount === 1 ? 'single' : ''}`}>
          {visiblePanels.nq && (
            <ChartPanel
              symbol="NQ"
              timeframe={timeframe}
              candles={nqCandles}
              annotations={nqSelectedAnnotations}
              focusTime={selectedFocusTime}
              focusRange={selectedFocusRange}
              onTimeframeChange={setTimeframe}
              syncedCrosshair={syncedCrosshair}
              onCrosshairMove={setSyncedCrosshair}
              onHide={() => setVisiblePanels((current) => ({ ...current, nq: false }))}
              statusText={status?.message}
            />
          )}
          {visiblePanels.es && (
            <ChartPanel
              symbol="ES"
              timeframe={timeframe}
              candles={esCandles}
              annotations={esSelectedAnnotations}
              focusTime={selectedFocusTime}
              focusRange={selectedFocusRange}
              onTimeframeChange={setTimeframe}
              syncedCrosshair={syncedCrosshair}
              onCrosshairMove={setSyncedCrosshair}
              onHide={() => setVisiblePanels((current) => ({ ...current, es: false }))}
              statusText={status?.message}
            />
          )}
        </div>
      </main>
      <SmtSidebar events={sortedEvents} selectedId={highlightedEvent?.id ?? null} onSelect={selectEvent} />
      <SettingsDrawer open={settingsOpen} settings={settings} onClose={() => setSettingsOpen(false)} onChange={updateSettings} />
      {toast && (
        <div className={`smt-toast ${toast.tone}`} role="status">
          <span>{toast.message}</span>
          <button type="button" onClick={() => setToast(null)}>Close</button>
        </div>
      )}
    </div>
  )
}
