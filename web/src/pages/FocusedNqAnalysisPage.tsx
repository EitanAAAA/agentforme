import { useEffect, useMemo, useState } from 'react'
import type { NqOneMinuteAnalysisDto, SmtEventDto } from '../types'
import { ChartPanel } from '../components/ChartPanel'

type Props = {
  event: SmtEventDto
  analysis?: NqOneMinuteAnalysisDto | null
  show: {
    bos: boolean
    fvg: boolean
    ifvg: boolean
    halfBox: boolean
    slTp: boolean
  }
  onToggle: (key: keyof Props['show']) => void
  onBack: () => void
}

type ReplayState = 'off' | 'armed' | 'playing' | 'paused' | 'done'

const replaySpeeds = [
  { label: 'Live', ms: 60_000 },
  { label: '1x', ms: 1000 },
  { label: '2x', ms: 500 },
  { label: '5x', ms: 200 },
] as const

export function FocusedNqAnalysisPage({ event, analysis, show, onToggle, onBack }: Props) {
  const [replayState, setReplayState] = useState<ReplayState>('paused')
  const [replayCursorIndex, setReplayCursorIndex] = useState<number | null>(null)
  const [replaySpeedIndex, setReplaySpeedIndex] = useState(1)
  const candles = analysis?.candles ?? []
  const replayCandle = replayCursorIndex === null ? null : candles[replayCursorIndex] ?? null
  const replayActive = replayState !== 'off'
  const replayCutoff = replayCandle ? new Date(replayCandle.timestamp).getTime() : null
  const visibleCandles = replayActive && replayCursorIndex !== null
    ? candles.slice(0, replayCursorIndex + 1)
    : candles
  const baseAnnotations = (analysis?.annotations ?? []).filter((annotation) => {
    if (annotation.kind === 'Bos') return show.bos
    if (annotation.kind === 'Fvg') return show.fvg
    if (annotation.kind === 'Ifvg') return show.ifvg
    if (annotation.kind === 'HalfBox') return show.halfBox
    if (annotation.kind === 'StopTakeProfit') return show.slTp
    return true
  })
  const annotations = baseAnnotations.filter((annotation) => {
    if (!replayActive || replayCutoff === null) {
      return false
    }

    return new Date(annotation.endTimestamp).getTime() <= replayCutoff
  })
  const replayNarration = useMemo(
    () => buildReplayNarration(replayState, replayCandle, baseAnnotations, replayCutoff),
    [baseAnnotations, replayCandle, replayCutoff, replayState],
  )
  const replayLog = useMemo(
    () => buildReplayLog(replayCandle, baseAnnotations, replayCutoff),
    [baseAnnotations, replayCandle, replayCutoff],
  )

  useEffect(() => {
    if (!analysis || candles.length === 0) {
      setReplayState('paused')
      setReplayCursorIndex(null)
      return
    }

    setReplayState('paused')
    setReplayCursorIndex(nearestCandleIndex(candles, event.timestamp))
  }, [analysis?.smtEventId, event.timestamp])

  useEffect(() => {
    if (replayState !== 'playing' || replayCursorIndex === null || candles.length === 0) {
      return
    }

    const timer = window.setTimeout(() => {
      setReplayCursorIndex((current) => {
        if (current === null) {
          return current
        }

        if (current >= candles.length - 1) {
          return current
        }

        return current + 1
      })
    }, replaySpeeds[replaySpeedIndex].ms)

    return () => window.clearTimeout(timer)
  }, [candles.length, replayCursorIndex, replaySpeedIndex, replayState])

  function armReplay() {
    if (candles.length === 0) {
      return
    }

    setReplayState('armed')
  }

  function toggleReplayPlay() {
    if (replayState === 'off') {
      resetReplay()
      return
    }

    if (replayState === 'armed') {
      setReplayState('playing')
      return
    }

    if (replayState === 'playing') {
      setReplayState('paused')
      return
    }

    if (replayCursorIndex !== null && replayCursorIndex >= candles.length - 1) {
      setReplayCursorIndex(nearestCandleIndex(candles, event.timestamp))
    }

    setReplayState('playing')
  }

  function resetReplay() {
    if (candles.length === 0) {
      setReplayState('paused')
      setReplayCursorIndex(null)
      return
    }

    setReplayState('paused')
    setReplayCursorIndex(nearestCandleIndex(candles, event.timestamp))
  }

  function selectReplayCut(_candle: typeof candles[number], index: number) {
    if (replayState !== 'armed') {
      return
    }

    setReplayCursorIndex(index)
    setReplayState('playing')
  }

  return (
    <main className="focused-page">
      <div className="focused-header">
        <div>
          <button type="button" className="ghost" onClick={onBack}>Back</button>
          <h1>NQ 1m Focus</h1>
          <p>{event.direction} {event.setupType} | {new Date(event.timestamp).toLocaleString()}</p>
        </div>
        <div className="focused-actions">
          <div className="replay-controls">
            <button className="replay-button active" type="button" onClick={toggleReplayPlay}>
              {replayState === 'playing' ? 'Pause Replay' : 'Play Replay'}
            </button>
            <button type="button" className={replayState === 'armed' ? 'active' : ''} onClick={armReplay}>Cut</button>
            <button type="button" onClick={resetReplay}>Reset</button>
            <select value={replaySpeedIndex} onChange={(item) => setReplaySpeedIndex(Number(item.target.value))} aria-label="Replay speed">
              {replaySpeeds.map((speed, index) => (
                <option key={speed.label} value={index}>{speed.label}</option>
              ))}
            </select>
          </div>
          <div className="toggle-strip">
            <button className={show.bos ? 'active' : ''} type="button" onClick={() => onToggle('bos')}>Show BOS</button>
            <button className={show.fvg ? 'active' : ''} type="button" onClick={() => onToggle('fvg')}>Show FVG</button>
            <button className={show.ifvg ? 'active' : ''} type="button" onClick={() => onToggle('ifvg')}>Show IFVG</button>
            <button className={show.halfBox ? 'active' : ''} type="button" onClick={() => onToggle('halfBox')}>Show 0.5 Box</button>
            <button className={show.slTp ? 'active' : ''} type="button" onClick={() => onToggle('slTp')}>Show SL/TP</button>
          </div>
        </div>
      </div>
      <div className="focused-body">
        <aside className="agent-chat">
          <div className="agent-chat-head">
            <strong>Agent Log</strong>
            <span>{replayNarration.status}</span>
          </div>
          <div className="agent-chat-current">
            <strong>{replayNarration.title}</strong>
            <span>{replayNarration.detail}</span>
          </div>
          <div className="agent-chat-feed">
            {replayLog.map((item) => (
              <div key={item.id} className={`agent-message ${item.tone}`}>
                <span className="agent-avatar">AI</span>
                <div className="agent-bubble">
                  <strong>{item.title}</strong>
                  <span>{item.detail}</span>
                </div>
              </div>
            ))}
            {replayLog.length === 0 && (
              <div className="agent-message muted">
                <span className="agent-avatar">AI</span>
                <div className="agent-bubble">
                  <strong>Waiting</strong>
                  <span>No candle selected yet.</span>
                </div>
              </div>
            )}
          </div>
        </aside>
        <ChartPanel
          symbol="NQ"
          timeframe="1m"
          candles={visibleCandles}
          annotations={annotations}
          statusText={replayNarration.status}
          onBarClick={selectReplayCut}
          showDrawingTools={false}
          compact
        />
      </div>
    </main>
  )
}

function nearestCandleIndex(candles: NonNullable<NqOneMinuteAnalysisDto['candles']>, timestamp: string) {
  if (candles.length === 0) {
    return 0
  }

  const target = new Date(timestamp).getTime()
  return candles.reduce((bestIndex, candle, candleIndex) => {
    const bestDistance = Math.abs(new Date(candles[bestIndex].timestamp).getTime() - target)
    const currentDistance = Math.abs(new Date(candle.timestamp).getTime() - target)
    return currentDistance < bestDistance ? candleIndex : bestIndex
  }, 0)
}

function buildReplayNarration(
  replayState: ReplayState,
  candle: NqOneMinuteAnalysisDto['candles'][number] | null,
  annotations: NqOneMinuteAnalysisDto['annotations'],
  cutoff: number | null,
) {
  if (replayState === 'armed') {
    return {
      title: 'Replay ready',
      detail: 'Click a 1m candle to cut the replay, or press Play to start at the SMT candle.',
      status: 'Replay armed - select bar',
    }
  }

  if (!candle || cutoff === null) {
    return {
      title: 'Replay',
      detail: 'Waiting for a replay cut.',
      status: 'Replay waiting',
    }
  }

  const revealed = annotations
    .filter((annotation) => new Date(annotation.endTimestamp).getTime() <= cutoff)
    .sort((a, b) => new Date(a.endTimestamp).getTime() - new Date(b.endTimestamp).getTime())
  const latest = revealed.at(-1)
  const time = new Date(candle.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const progress = `${revealed.length} steps visible | current candle ${time}`

  if (!latest) {
    return {
      title: `Reading ${time}`,
      detail: 'No BOS/FVG/IFVG confirmed yet. Waiting for structure to print.',
      status: `Replay ${time}`,
    }
  }

  const latestTime = new Date(latest.endTimestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const detailByKind: Record<string, string> = {
    Smt: `SMT context is active from ${latestTime}.`,
    SmtExtreme: `SMT extreme printed at ${latestTime}.`,
    Bos: `BOS confirmed at ${latestTime}.`,
    Fvg: `Original imbalance marked at ${latestTime}.`,
    Ifvg: `IFVG confirmed at ${latestTime}; the original imbalance failed.`,
    HalfBox: `0.5 box is available after BOS + IFVG at ${latestTime}.`,
    StopTakeProfit: `Visual entry plan active at ${latestTime}: stop beyond SMT invalidation with the configured buffer.`,
  }

  return {
    title: `${latest.kind} detected`,
    detail: `${detailByKind[latest.kind] ?? `${latest.label} at ${latestTime}.`} ${progress}`,
    status: replayState === 'done' ? `Replay done - ${time}` : `Replay ${time}`,
  }
}

function buildReplayLog(
  candle: NqOneMinuteAnalysisDto['candles'][number] | null,
  annotations: NqOneMinuteAnalysisDto['annotations'],
  cutoff: number | null,
) {
  if (!candle || cutoff === null) {
    return []
  }

  const time = new Date(candle.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const visible = annotations
    .filter((annotation) => new Date(annotation.endTimestamp).getTime() <= cutoff)
    .sort((a, b) => new Date(a.endTimestamp).getTime() - new Date(b.endTimestamp).getTime())
  const direction = annotations.find((annotation) => annotation.direction === 'Bearish' || annotation.direction === 'Bullish')?.direction ?? 'Bearish'
  const isBearish = direction === 'Bearish'
  const hasSmt = visible.some((annotation) => annotation.kind === 'Smt' || annotation.kind === 'SmtExtreme')
  const bos = visible.find((annotation) => annotation.kind === 'Bos')
  const fvg = visible.find((annotation) => annotation.kind === 'Fvg')
  const ifvg = visible.find((annotation) => annotation.kind === 'Ifvg')
  const halfBox = visible.find((annotation) => annotation.kind === 'HalfBox')
  const items = [{
    id: `candle-${candle.timestamp}`,
    title: `Candle ${time}`,
    detail: `O ${candle.open.toFixed(2)} H ${candle.high.toFixed(2)} L ${candle.low.toFixed(2)} C ${candle.close.toFixed(2)}`,
    tone: 'info',
  }]

  if (hasSmt && !bos) {
    items.push({
      id: `bos-watch-${time}`,
      title: 'Searching BOS',
      detail: isBearish
        ? 'Bearish SMT active. Watching the last confirmed pre-SMT swing low; a wick break below it confirms BOS.'
        : 'Bullish SMT active. Watching the last confirmed pre-SMT swing high; a wick break above it confirms BOS.',
      tone: 'action',
    })
  }

  if (bos && !fvg) {
    items.push({
      id: `fvg-watch-${time}`,
      title: 'Searching FVG',
      detail: isBearish
        ? 'BOS printed. Looking back into the push up for the bullish FVG that can fail into bearish IFVG.'
        : 'BOS printed. Looking back into the push down for the bearish FVG that can fail into bullish IFVG.',
      tone: 'action',
    })
  }

  if (fvg && !ifvg) {
    items.push({
      id: `ifvg-watch-${time}`,
      title: 'Searching IFVG',
      detail: isBearish
        ? 'Bullish FVG located. Waiting for a bearish candle to close through the gap.'
        : 'Bearish FVG located. Waiting for a bullish candle to close through the gap.',
      tone: 'action',
    })
  }

  if (ifvg && !halfBox) {
    items.push({
      id: `box-watch-${time}`,
      title: 'Building 0.5 Box',
      detail: isBearish
        ? 'IFVG confirmed. Measuring from SMT high to the lowest wick of the displacement.'
        : 'IFVG confirmed. Measuring from SMT low to the highest wick of the displacement.',
      tone: 'success',
    })
  }

  for (const annotation of visible.slice(-6)) {
    const annotationTime = new Date(annotation.endTimestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    items.push({
      id: `${annotation.kind}-${annotation.endTimestamp}-${annotation.label}`,
      title: replayLogTitle(annotation.kind),
      detail: replayLogDetail(annotation.kind, annotation.label, annotationTime),
      tone: replayLogTone(annotation.kind),
    })
  }

  if (visible.some((annotation) => annotation.kind === 'Smt')) {
    items.push({
      id: `cancel-watch-${time}`,
      title: 'Cancel Watch',
      detail: 'If the failed side takes the SMT level before confirmation, cancel the idea.',
      tone: 'danger',
    })
  }

  return items.slice(-9)
}

function replayLogTitle(kind: string) {
  const titles: Record<string, string> = {
    Smt: 'SMT Context',
    SmtExtreme: 'SMT Point',
    Bos: 'BOS',
    Fvg: 'FVG',
    Ifvg: 'IFVG',
    HalfBox: '0.5 Box',
    StopTakeProfit: 'Entry Plan',
  }
  return titles[kind] ?? kind
}

function replayLogDetail(kind: string, label: string, time: string) {
  const details: Record<string, string> = {
    Smt: `SMT is active at ${time}; wait for structure, do not use future candles.`,
    SmtExtreme: `SMT extreme is marked at ${time}.`,
    Bos: `Break of structure confirmed at ${time}.`,
    Fvg: `Original fair value gap from the move into SMT is marked at ${time}.`,
    Ifvg: `Inverted FVG confirmed at ${time}; imbalance flipped in the setup direction.`,
    HalfBox: `0.5 box is available at ${time} after BOS + IFVG.`,
    StopTakeProfit: `Visual entry/SL/TP plan becomes available at ${time}. Stop uses the configured buffer beyond SMT invalidation; no trades are executed.`,
  }
  return details[kind] ?? `${label} at ${time}.`
}

function replayLogTone(kind: string) {
  if (kind === 'Bos' || kind === 'Fvg' || kind === 'Ifvg') {
    return 'action'
  }

  if (kind === 'StopTakeProfit') {
    return 'success'
  }

  return 'info'
}
