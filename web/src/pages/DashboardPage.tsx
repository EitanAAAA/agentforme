import { useEffect, useMemo, useState } from 'react'
import { api, createMarketConnection } from '../api'
import { ChartPanel, type ChartCrosshairSync } from '../components/ChartPanel'
import { SettingsDrawer } from '../components/SettingsDrawer'
import { SmtSidebar } from '../components/SmtSidebar'
import { StatusBadge } from '../components/StatusBadge'
import type {
  AppSettingsDto,
  CandleDto,
  DataStatusDto,
  NqOneMinuteAnalysisDto,
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

export function DashboardPage() {
  const [status, setStatus] = useState<DataStatusDto | null>(null)
  const [settings, setSettings] = useState<AppSettingsDto | null>(null)
  const [esCandles, setEsCandles] = useState<CandleDto[]>([])
  const [nqCandles, setNqCandles] = useState<CandleDto[]>([])
  const [events, setEvents] = useState<SmtEventDto[]>([])
  const [selectedEvent, setSelectedEvent] = useState<SmtEventDto | null>(null)
  const [focusedAnalysis, setFocusedAnalysis] = useState<NqOneMinuteAnalysisDto | null>(null)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [toggles, setToggles] = useState(defaultToggles)
  const [visiblePanels, setVisiblePanels] = useState({ nq: true, es: true })
  const [timeframe, setTimeframe] = useState('15m')
  const [syncedCrosshair, setSyncedCrosshair] = useState<ChartCrosshairSync | null>(null)
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
    connection.on('FocusedAnalysisUpdated', (analysis: NqOneMinuteAnalysisDto) => {
      setFocusedAnalysis((current) => current?.smtEventId === analysis.smtEventId ? analysis : current)
    })
    connection.start().catch(() => undefined)
    return () => {
      connection.stop().catch(() => undefined)
    }
  }, [timeframe])

  const sortedEvents = useMemo(
    () => [...events].sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime()),
    [events],
  )

  async function selectEvent(event: SmtEventDto) {
    setSelectedEvent(event)
    setFocusedAnalysis(null)
    const analysis = await api.getFocusedAnalysis(event.id)
    setFocusedAnalysis(analysis)
  }

  async function updateSettings(next: AppSettingsDto) {
    setSettings(next)
    const saved = await api.updateSettings(next)
    setSettings(saved)
  }

  if (selectedEvent) {
    return (
      <div className="app-shell">
        <FocusedNqAnalysisPage
          event={selectedEvent}
          analysis={focusedAnalysis}
          show={toggles}
          onToggle={(key) => setToggles((current) => ({ ...current, [key]: !current[key] }))}
          onBack={() => {
            setSelectedEvent(null)
            setFocusedAnalysis(null)
          }}
        />
        <SmtSidebar events={sortedEvents} selectedId={selectedEvent.id} onSelect={selectEvent} />
      </div>
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
              onTimeframeChange={setTimeframe}
              syncedCrosshair={syncedCrosshair}
              onCrosshairMove={setSyncedCrosshair}
              onHide={() => setVisiblePanels((current) => ({ ...current, es: false }))}
              statusText={status?.message}
            />
          )}
        </div>
      </main>
      <SmtSidebar events={sortedEvents} selectedId={null} onSelect={selectEvent} />
      <SettingsDrawer open={settingsOpen} settings={settings} onClose={() => setSettingsOpen(false)} onChange={updateSettings} />
    </div>
  )
}
