import { useCallback, useEffect, useMemo, useRef, useState, type PointerEvent } from 'react'
import {
  CandlestickSeries,
  createChart,
  type CandlestickData,
  type IChartApi,
  type ISeriesApi,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts'
import type { CandleDto, ChartAnnotationDto } from '../types'
import { AnnotationLayer, type OverlayBox, type OverlayLine } from './AnnotationLayer'
import { ChartToolbar } from './ChartToolbar'

type Tool = 'select' | 'line' | 'box' | 'halfbox' | 'sltp' | 'text' | 'measure'
type DrawingType = 'line' | 'box' | 'halfbox' | 'sltp' | 'text' | 'measure'

type Drawing = {
  id: string
  type: DrawingType
  time1: number
  time2: number
  price1: number
  price2: number
  color: string
  lineWidth: number
  opacity: number
  dashed: boolean
  label: string
  locked: boolean
  hidden: boolean
}

type DragAction =
  | { kind: 'draw'; id: string }
  | { kind: 'move'; id: string; startTime: number; startPrice: number; original: Drawing }
  | { kind: 'resize'; id: string; handle: string }
  | null

type DataPoint = {
  x: number
  y: number
  time: number
  price: number
}

export type ChartCrosshairSync = {
  source: 'ES' | 'NQ'
  time: Time | null
  price: number | null
}

export type ChartFocusRange = {
  start: string
  end: string
}

type Props = {
  symbol: 'ES' | 'NQ'
  timeframe: string
  candles: CandleDto[]
  annotations?: ChartAnnotationDto[]
  compact?: boolean
  hidden?: boolean
  onHide?: () => void
  statusText?: string
  onTimeframeChange?: (timeframe: string) => void
  syncedCrosshair?: ChartCrosshairSync | null
  onCrosshairMove?: (sync: ChartCrosshairSync) => void
  focusTime?: string | null
  focusRange?: ChartFocusRange | null
}

function toTimestamp(timestamp: string): UTCTimestamp {
  return Math.floor(new Date(timestamp).getTime() / 1000) as UTCTimestamp
}

function toSeriesData(candles: CandleDto[]): CandlestickData[] {
  return candles.map((candle) => ({
    time: toTimestamp(candle.timestamp),
    open: candle.open,
    high: candle.high,
    low: candle.low,
    close: candle.close,
  }))
}

export function ChartPanel({
  symbol,
  timeframe,
  candles,
  annotations = [],
  compact = false,
  onHide,
  statusText,
  onTimeframeChange,
  syncedCrosshair,
  onCrosshairMove,
  focusTime,
  focusRange,
}: Props) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const svgRef = useRef<SVGSVGElement | null>(null)
  const chartRef = useRef<IChartApi | null>(null)
  const seriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null)
  const autoScrollRef = useRef(true)
  const didSetInitialRangeRef = useRef(false)
  const previousTimeframeRef = useRef(timeframe)
  const dataLengthRef = useRef(0)
  const rafRef = useRef<number | null>(null)
  const crosshairRafRef = useRef<number | null>(null)
  const lastCrosshairUpdateRef = useRef(0)
  const ohlcRef = useRef('O -- H -- L -- C --')
  const dragActionRef = useRef<DragAction>(null)
  const drawingsRef = useRef<Drawing[]>([])
  const candlesRef = useRef<CandleDto[]>([])
  const annotationsRef = useRef<ChartAnnotationDto[]>([])
  const suppressCrosshairSyncRef = useRef(false)
  const appliedFocusKeyRef = useRef<string | null>(null)
  const [ohlc, setOhlc] = useState('O -- H -- L -- C --')
  const [overlayVersion, setOverlayVersion] = useState(0)
  const [tool, setTool] = useState<Tool>('select')
  const [drawings, setDrawings] = useState<Drawing[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [floating, setFloating] = useState<{ left: number; top: number } | null>(null)
  const data = useMemo(() => toSeriesData(candles), [candles])
  const lastCandle = candles.at(-1)
  const title = symbol === 'NQ'
    ? `NASDAQ 100 E-mini Futures - ${timeframe} - CME`
    : `S&P 500 E-mini Futures - ${timeframe} - CME`

  const scheduleOverlay = useCallback(() => {
    if (annotationsRef.current.length === 0 && drawingsRef.current.length === 0) {
      return
    }

    if (rafRef.current !== null) {
      return
    }

    rafRef.current = requestAnimationFrame(() => {
      rafRef.current = null
      setOverlayVersion((value) => value + 1)
    })
  }, [])

  const commitDrawings = useCallback((next: Drawing[]) => {
    drawingsRef.current = next
    setDrawings(next)
    scheduleOverlay()
  }, [scheduleOverlay])

  useEffect(() => {
    drawingsRef.current = drawings
  }, [drawings])

  useEffect(() => {
    annotationsRef.current = annotations
    scheduleOverlay()
  }, [annotations, scheduleOverlay])

  useEffect(() => {
    if (!containerRef.current) {
      return
    }

    const chart = createChart(containerRef.current, {
      autoSize: true,
      layout: {
        background: { color: '#eef4ff' },
        textColor: '#111827',
      },
      grid: {
        vertLines: { color: '#dce5f3' },
        horzLines: { color: '#dce5f3' },
      },
      rightPriceScale: {
        borderColor: '#d8dee8',
        scaleMargins: { top: 0.08, bottom: 0.18 },
      },
      timeScale: {
        borderColor: '#d8dee8',
        timeVisible: true,
        secondsVisible: timeframe === '30s',
        rightOffset: 8,
        barSpacing: 9,
        tickMarkFormatter: (time: Time) => formatAxisTime(time),
      },
      localization: {
        timeFormatter: (time: Time) => formatCrosshairTime(time),
      },
      crosshair: {
        mode: 0,
        vertLine: { color: '#8b95a7', style: 2, width: 1, labelBackgroundColor: '#111827' },
        horzLine: { color: '#8b95a7', style: 2, width: 1, labelBackgroundColor: '#111827' },
      },
      handleScroll: {
        mouseWheel: false,
        pressedMouseMove: true,
        horzTouchDrag: true,
        vertTouchDrag: false,
      },
      handleScale: {
        axisPressedMouseMove: true,
        mouseWheel: true,
        pinch: true,
      },
    })
    const series = chart.addSeries(CandlestickSeries, {
      upColor: '#089981',
      downColor: '#f23645',
      borderUpColor: '#089981',
      borderDownColor: '#f23645',
      wickUpColor: '#089981',
      wickDownColor: '#f23645',
    })

    chartRef.current = chart
    seriesRef.current = series

    const rangeSubscription = () => {
      scheduleOverlay()
      const range = chart.timeScale().getVisibleLogicalRange()
      if (range && dataLengthRef.current > 0) {
        autoScrollRef.current = range.to >= dataLengthRef.current - 1.5
      }
    }

    chart.timeScale().subscribeVisibleLogicalRangeChange(rangeSubscription)
    chart.subscribeCrosshairMove((param) => {
      const value = param.seriesData.get(series) as CandlestickData | undefined
      if (!suppressCrosshairSyncRef.current) {
        onCrosshairMove?.({
          source: symbol,
          time: param.time ?? null,
          price: value?.close ?? null,
        })
      }

      if (crosshairRafRef.current !== null) {
        return
      }

      crosshairRafRef.current = requestAnimationFrame(() => {
        crosshairRafRef.current = null
        const now = Date.now()
        if (now - lastCrosshairUpdateRef.current < 80) {
          return
        }
        lastCrosshairUpdateRef.current = now
        let nextOhlc = 'O -- H -- L -- C --'
        if (!value) {
          const last = candlesRef.current.at(-1)
          nextOhlc = last ? formatOhlc(last) : 'O -- H -- L -- C --'
          if (nextOhlc !== ohlcRef.current) {
            ohlcRef.current = nextOhlc
            setOhlc(nextOhlc)
          }
          return
        }

        const time = typeof param.time === 'number'
          ? new Date(param.time * 1000).toLocaleString([], { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' })
          : ''
        nextOhlc = `${time}  O ${value.open.toFixed(2)}  H ${value.high.toFixed(2)}  L ${value.low.toFixed(2)}  C ${value.close.toFixed(2)}`
        if (nextOhlc !== ohlcRef.current) {
          ohlcRef.current = nextOhlc
          setOhlc(nextOhlc)
        }
      })
    })

    const resizeObserver = new ResizeObserver(scheduleOverlay)
    resizeObserver.observe(containerRef.current)

    return () => {
      if (rafRef.current !== null) {
        cancelAnimationFrame(rafRef.current)
      }
      if (crosshairRafRef.current !== null) {
        cancelAnimationFrame(crosshairRafRef.current)
      }
      resizeObserver.disconnect()
      chart.timeScale().unsubscribeVisibleLogicalRangeChange(rangeSubscription)
      chart.remove()
    }
  }, [onCrosshairMove, scheduleOverlay, symbol, timeframe])

  useEffect(() => {
    candlesRef.current = candles
    dataLengthRef.current = data.length
    seriesRef.current?.setData(data)
    if (candles.length > 0) {
      const nextOhlc = formatOhlc(candles[candles.length - 1])
      ohlcRef.current = nextOhlc
      setOhlc(nextOhlc)
    }
    if ((!didSetInitialRangeRef.current || previousTimeframeRef.current !== timeframe) && data.length > 0) {
      const visibleBars = visibleBarsForTimeframe(timeframe)
      const to = data.length - 1
      chartRef.current?.timeScale().setVisibleLogicalRange({ from: Math.max(0, to - visibleBars), to: to + 8 })
      didSetInitialRangeRef.current = true
      previousTimeframeRef.current = timeframe
    } else if (autoScrollRef.current) {
      chartRef.current?.timeScale().scrollToRealTime()
    }
    scheduleOverlay()
  }, [candles, data, scheduleOverlay, timeframe])

  useEffect(() => {
    const chart = chartRef.current
    const series = seriesRef.current
    if (!chart || !series || !syncedCrosshair || syncedCrosshair.source === symbol) {
      return
    }

    if (syncedCrosshair.time === null || syncedCrosshair.price === null) {
      chart.clearCrosshairPosition()
      return
    }

    const price = nearestPriceForTime(candlesRef.current, syncedCrosshair.time) ?? syncedCrosshair.price
    suppressCrosshairSyncRef.current = true
    chart.setCrosshairPosition(price, syncedCrosshair.time, series)
    requestAnimationFrame(() => {
      suppressCrosshairSyncRef.current = false
    })
  }, [syncedCrosshair, symbol])

  useEffect(() => {
    if (!focusTime && !focusRange) {
      appliedFocusKeyRef.current = null
      return
    }

    if (!chartRef.current || !seriesRef.current || candles.length === 0) {
      return
    }

    const focusKey = `${focusRange?.start ?? ''}|${focusRange?.end ?? ''}|${focusTime ?? ''}`
    if (appliedFocusKeyRef.current === focusKey) {
      return
    }

    const targetTime = new Date(focusTime ?? focusRange?.end ?? candles.at(-1)?.timestamp ?? '').getTime()
    const targetIndex = nearestIndexForTime(candles, targetTime)
    const focusStartIndex = focusRange ? nearestIndexForTime(candles, new Date(focusRange.start).getTime()) : targetIndex
    const focusEndIndex = focusRange ? nearestIndexForTime(candles, new Date(focusRange.end).getTime()) : targetIndex
    const startIndex = Math.min(focusStartIndex, focusEndIndex)
    const endIndex = Math.max(focusStartIndex, focusEndIndex)
    const rangeWidth = Math.max(12, endIndex - startIndex + 1)
    const buffer = Math.max(8, Math.ceil(rangeWidth * 0.25))
    const from = Math.max(0, startIndex - buffer)
    const to = Math.min(candles.length - 1, endIndex + buffer)
    autoScrollRef.current = false
    chartRef.current.timeScale().setVisibleLogicalRange({ from, to: to + 4 })
    appliedFocusKeyRef.current = focusKey

    const candle = candles[targetIndex]
    if (candle) {
      suppressCrosshairSyncRef.current = true
      chartRef.current.setCrosshairPosition(candle.close, toTimestamp(candle.timestamp), seriesRef.current)
      requestAnimationFrame(() => {
        suppressCrosshairSyncRef.current = false
      })
    }
    scheduleOverlay()
  }, [candles, focusRange, focusTime, scheduleOverlay])

  const overlay = useMemo(() => {
    void overlayVersion
    return projectAnnotations(annotations, drawings, selectedId, chartRef.current, seriesRef.current)
  }, [annotations, drawings, overlayVersion, selectedId])

  useEffect(() => {
    if (!selectedId) {
      setFloating(null)
      return
    }

    const selected = [...overlay.boxes, ...overlay.lines].find((item) => item.id === selectedId)
    if (!selected) {
      setFloating(null)
      return
    }

    if ('left' in selected) {
      setFloating({ left: selected.left + Math.min(selected.width, 220) / 2, top: Math.max(8, selected.top - 45) })
    } else {
      setFloating({ left: (selected.x1 + selected.x2) / 2, top: Math.max(8, Math.min(selected.y1, selected.y2) - 45) })
    }
  }, [overlay, selectedId])

  const fit = () => {
    autoScrollRef.current = false
    chartRef.current?.timeScale().fitContent()
    scheduleOverlay()
  }

  const latest = () => {
    autoScrollRef.current = true
    chartRef.current?.timeScale().scrollToRealTime()
    scheduleOverlay()
  }

  const reset = () => {
    if (!chartRef.current || data.length === 0) {
      return
    }

    const to = data.length - 1
    autoScrollRef.current = false
    chartRef.current.timeScale().setVisibleLogicalRange({ from: Math.max(0, to - 90), to })
    scheduleOverlay()
  }

  const onLayerPointerDown = (event: PointerEvent<SVGSVGElement>) => {
    if (tool === 'select') {
      return
    }

    const point = getDataPoint(event)
    if (!point) {
      return
    }

    event.currentTarget.setPointerCapture(event.pointerId)
    const id = `${tool}-${Date.now()}`
    const drawing = createDrawing(id, tool, point)
    dragActionRef.current = { kind: 'draw', id }
    setSelectedId(id)
    commitDrawings([...drawingsRef.current, drawing])
  }

  const onLayerPointerMove = (event: PointerEvent<SVGSVGElement>) => {
    const action = dragActionRef.current
    if (!action) {
      return
    }

    const point = getDataPoint(event)
    if (!point) {
      return
    }

    if (action.kind === 'draw') {
      commitDrawings(drawingsRef.current.map((drawing) => drawing.id === action.id ? { ...drawing, time2: point.time, price2: point.price } : drawing))
      return
    }

    if (action.kind === 'move') {
      const timeDelta = point.time - action.startTime
      const priceDelta = point.price - action.startPrice
      commitDrawings(drawingsRef.current.map((drawing) => drawing.id === action.id
        ? {
            ...action.original,
            time1: action.original.time1 + timeDelta,
            time2: action.original.time2 + timeDelta,
            price1: action.original.price1 + priceDelta,
            price2: action.original.price2 + priceDelta,
          }
        : drawing))
      return
    }

    commitDrawings(drawingsRef.current.map((drawing) => drawing.id === action.id ? resizeDrawing(drawing, action.handle, point) : drawing))
  }

  const onLayerPointerUp = (event: PointerEvent<SVGSVGElement>) => {
    const action = dragActionRef.current
    dragActionRef.current = null
    if (action?.kind === 'draw') {
      setTool('select')
    }
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId)
    }
  }

  const onSelect = (id: string, event: PointerEvent) => {
    event.stopPropagation()
    setSelectedId(id)
    if (tool !== 'select') {
      return
    }

    const drawing = drawingsRef.current.find((item) => item.id === id)
    const point = getDataPoint(event)
    if (!drawing || drawing.locked || !point) {
      return
    }

    dragActionRef.current = { kind: 'move', id, startTime: point.time, startPrice: point.price, original: drawing }
  }

  const onHandle = (id: string, handle: string, event: PointerEvent) => {
    event.stopPropagation()
    const drawing = drawingsRef.current.find((item) => item.id === id)
    if (!drawing || drawing.locked) {
      return
    }

    dragActionRef.current = { kind: 'resize', id, handle }
  }

  const selectedDrawing = drawings.find((drawing) => drawing.id === selectedId)

  return (
    <section className={`chart-panel ${compact ? 'compact' : ''}`}>
      <ChartToolbar
        title={title}
        subtitle={ohlc}
        volume={lastCandle ? compactVolume(lastCandle.volume) : undefined}
        status={statusText}
        onReset={reset}
        onFit={fit}
        onLatest={latest}
      />
      <div className="chart-shell" onDoubleClick={reset}>
        <DrawingToolbar
          tool={tool}
          selectedDrawing={selectedDrawing}
          onTool={setTool}
          onLock={() => updateSelected((drawing) => ({ ...drawing, locked: !drawing.locked }))}
          onToggleHidden={() => updateSelected((drawing) => ({ ...drawing, hidden: !drawing.hidden }))}
          onDelete={() => deleteSelected()}
        />
        <ShortcutPalette tool={tool} onTool={setTool} />
        <div ref={containerRef} className="chart-root" />
        <AnnotationLayer
          boxes={overlay.boxes}
          lines={overlay.lines}
          mode={tool === 'select' ? 'select' : 'draw'}
          selectedId={selectedId}
          onPointerDown={onLayerPointerDown}
          onPointerMove={onLayerPointerMove}
          onPointerUp={onLayerPointerUp}
          onSelect={onSelect}
          onHandle={onHandle}
        />
        {selectedDrawing && floating && (
          <FloatingToolbar
            left={floating.left}
            top={floating.top}
            drawing={selectedDrawing}
            onUpdate={(patch) => updateSelected((drawing) => ({ ...drawing, ...patch }))}
            onDuplicate={() => duplicateSelected()}
            onDelete={() => deleteSelected()}
          />
        )}
      </div>
      <div className="timeframe-strip">
        <div className="timeframe-buttons">
          {(['15m', '5m', '1m'] as const).map((next) => (
            <button key={next} type="button" className={timeframe === next ? 'active' : ''} onClick={() => onTimeframeChange?.(next)}>
              {next}
            </button>
          ))}
        </div>
        <div className="chart-footer-actions">
          <button type="button" onClick={fit}>Fit</button>
          <button type="button" onClick={latest}>Latest</button>
          {onHide && <button type="button" className="panel-hide-button" onClick={onHide}>Hide {symbol}</button>}
        </div>
      </div>
    </section>
  )

  function getDataPoint(event: PointerEvent): DataPoint | null {
    const chart = chartRef.current
    const series = seriesRef.current
    const svg = svgRef.current ?? event.currentTarget.closest('.chart-shell')?.querySelector('svg')
    if (!chart || !series || !svg) {
      return null
    }

    svgRef.current = svg as SVGSVGElement
    const rect = svg.getBoundingClientRect()
    const x = event.clientX - rect.left
    const y = event.clientY - rect.top
    const time = chart.timeScale().coordinateToTime(x)
    const price = series.coordinateToPrice(y)
    if (time === null || price === null) {
      return null
    }

    return { x, y, time: normalizeTime(time), price }
  }

  function updateSelected(update: (drawing: Drawing) => Drawing) {
    if (!selectedId) {
      return
    }

    commitDrawings(drawingsRef.current.map((drawing) => drawing.id === selectedId ? update(drawing) : drawing))
  }

  function deleteSelected() {
    if (!selectedId) {
      return
    }

    commitDrawings(drawingsRef.current.filter((drawing) => drawing.id !== selectedId))
    setSelectedId(null)
  }

  function duplicateSelected() {
    if (!selectedDrawing) {
      return
    }

    const copy = {
      ...selectedDrawing,
      id: `${selectedDrawing.type}-${Date.now()}`,
      time1: selectedDrawing.time1 + 60,
      time2: selectedDrawing.time2 + 60,
    }
    commitDrawings([...drawingsRef.current, copy])
    setSelectedId(copy.id)
  }
}

function ShortcutPalette({ tool, onTool }: { tool: Tool; onTool: (tool: Tool) => void }) {
  return (
    <div className="quick-tool-palette">
      <span className="drag-dots" aria-hidden="true" />
      <button type="button" className={tool === 'box' ? 'active' : ''} title="Rectangle/FVG box" onClick={() => onTool('box')}>
        <span className="tool-icon icon-box" />
        <span>Box</span>
      </button>
      <button type="button" className={tool === 'line' ? 'active' : ''} title="Line" onClick={() => onTool('line')}>
        <span className="tool-icon icon-line" />
        <span>Line</span>
      </button>
      <button type="button" className={tool === 'halfbox' ? 'active' : ''} title="0.5 box" onClick={() => onTool('halfbox')}>
        <span className="tool-icon icon-levels" />
        <span>0.5</span>
      </button>
      <button type="button" className={tool === 'sltp' ? 'active' : ''} title="Stop loss / take profit" onClick={() => onTool('sltp')}>
        <span className="tool-icon icon-risk" />
        <span>SL/TP</span>
      </button>
    </div>
  )
}

function DrawingToolbar({
  tool,
  selectedDrawing,
  onTool,
  onLock,
  onToggleHidden,
  onDelete,
}: {
  tool: Tool
  selectedDrawing?: Drawing
  onTool: (tool: Tool) => void
  onLock: () => void
  onToggleHidden: () => void
  onDelete: () => void
}) {
  return (
    <div className="chart-tool-rail">
      <ToolButton active={tool === 'select'} label="Cursor/select" text="C" onClick={() => onTool('select')} />
      <ToolButton active={tool === 'line'} label="Line" text="L" onClick={() => onTool('line')} />
      <ToolButton active={tool === 'box'} label="Rectangle/FVG box" text="R" onClick={() => onTool('box')} />
      <ToolButton active={tool === 'halfbox'} label="0.5 box" text="0.5" onClick={() => onTool('halfbox')} />
      <ToolButton active={tool === 'sltp'} label="Stop loss / take profit" text="SL" onClick={() => onTool('sltp')} />
      <ToolButton active={tool === 'text'} label="Text label" text="T" onClick={() => onTool('text')} />
      <ToolButton active={tool === 'measure'} label="Measure/ruler" text="M" onClick={() => onTool('measure')} />
      <ToolButton label="Magnet" text="Mag" onClick={() => undefined} />
      <ToolButton active={selectedDrawing?.locked} disabled={!selectedDrawing} label="Lock/unlock selected drawing" text="Lock" onClick={onLock} />
      <ToolButton active={selectedDrawing?.hidden} disabled={!selectedDrawing} label="Hide/show selected drawing" text="Eye" onClick={onToggleHidden} />
      <ToolButton disabled={!selectedDrawing} label="Delete selected drawing" text="Del" onClick={onDelete} />
    </div>
  )
}

function ToolButton({
  active,
  disabled,
  label,
  text,
  onClick,
}: {
  active?: boolean
  disabled?: boolean
  label: string
  text: string
  onClick: () => void
}) {
  return (
    <button type="button" className={active ? 'active' : ''} disabled={disabled} title={label} onClick={onClick}>
      {text}
    </button>
  )
}

function FloatingToolbar({
  left,
  top,
  drawing,
  onUpdate,
  onDuplicate,
  onDelete,
}: {
  left: number
  top: number
  drawing: Drawing
  onUpdate: (patch: Partial<Drawing>) => void
  onDuplicate: () => void
  onDelete: () => void
}) {
  return (
    <div className="floating-tools" style={{ left, top }}>
      <input aria-label="color" type="color" value={drawing.color} onChange={(event) => onUpdate({ color: event.target.value })} />
      <select aria-label="line width" value={drawing.lineWidth} onChange={(event) => onUpdate({ lineWidth: Number(event.target.value) })}>
        <option value={1}>1px</option>
        <option value={2}>2px</option>
        <option value={3}>3px</option>
      </select>
      <select aria-label="opacity" value={drawing.opacity} onChange={(event) => onUpdate({ opacity: Number(event.target.value) })}>
        <option value={0.25}>25%</option>
        <option value={0.5}>50%</option>
        <option value={0.75}>75%</option>
        <option value={1}>100%</option>
      </select>
      <button type="button" className={drawing.dashed ? 'active' : ''} onClick={() => onUpdate({ dashed: !drawing.dashed })}>Dash</button>
      <input aria-label="label" className="label-input" value={drawing.label} onChange={(event) => onUpdate({ label: event.target.value })} />
      <button type="button" onClick={onDuplicate}>Copy</button>
      <button type="button" onClick={onDelete}>Del</button>
    </div>
  )
}

function projectAnnotations(
  annotations: ChartAnnotationDto[],
  drawings: Drawing[],
  selectedId: string | null,
  chart: IChartApi | null,
  series: ISeriesApi<'Candlestick'> | null,
) {
  if (!chart || !series) {
    return { boxes: [] as OverlayBox[], lines: [] as OverlayLine[] }
  }

  const boxes: OverlayBox[] = []
  const lines: OverlayLine[] = []
  for (const annotation of annotations) {
    const x1 = chart.timeScale().timeToCoordinate(toTimestamp(annotation.startTimestamp))
    const x2 = chart.timeScale().timeToCoordinate(toTimestamp(annotation.endTimestamp))
    if (x1 === null || x2 === null) {
      continue
    }

    if (annotation.kind === 'Bos') {
      const y = series.priceToCoordinate(annotation.secondaryPrice ?? annotation.price)
      if (y !== null) {
        lines.push({ id: `backend-${annotation.kind}-${annotation.endTimestamp}`, kind: 'bos', label: 'BOS', x1, x2, y1: y, y2: y, color: '#2563eb' })
      }
      continue
    }

    if (annotation.kind === 'Smt' && annotation.secondaryPrice !== null && annotation.secondaryPrice !== undefined) {
      const y1 = series.priceToCoordinate(annotation.price)
      const y2 = series.priceToCoordinate(annotation.secondaryPrice)
      if (y1 !== null && y2 !== null) {
        lines.push({
          id: `backend-${annotation.kind}-${annotation.label}-${annotation.endTimestamp}`,
          kind: 'smt',
          label: annotation.label || 'SMT H/L',
          x1,
          x2,
          y1,
          y2,
          color: annotation.direction === 'Bearish' ? '#ef4444' : '#089981',
          lineWidth: 2.8,
        })
      }
      continue
    }

    if (annotation.kind === 'HalfBox' && annotation.secondaryPrice !== null && annotation.secondaryPrice !== undefined && annotation.tertiaryPrice !== null && annotation.tertiaryPrice !== undefined) {
      addHalfBox(boxes, annotation, chart, series, `backend-${annotation.kind}-${annotation.startTimestamp}`, selectedId)
      continue
    }

    if (annotation.kind === 'StopTakeProfit' && annotation.secondaryPrice !== null && annotation.secondaryPrice !== undefined && annotation.tertiaryPrice !== null && annotation.tertiaryPrice !== undefined) {
      addPriceBox(boxes, series, x1, x2, annotation.price, annotation.secondaryPrice, annotation.direction, 'trade-risk', 'Stop Loss', '#f23645')
      addPriceBox(boxes, series, x1, x2, annotation.price, annotation.tertiaryPrice, annotation.direction, 'trade-reward', 'Take Profit', '#089981')
      continue
    }

    if (annotation.secondaryPrice !== null && annotation.secondaryPrice !== undefined) {
      addPriceBox(
        boxes,
        series,
        x1,
        x2,
        annotation.price,
        annotation.secondaryPrice,
        annotation.direction,
        annotation.kind.toLowerCase(),
        annotation.kind === 'Ifvg' ? 'IFVG' : 'FVG',
        annotation.kind === 'Ifvg' ? '#7c3aed' : '#2563eb',
      )
    }
  }

  for (const drawing of drawings) {
    if (drawing.hidden) {
      continue
    }

    const x1 = chart.timeScale().timeToCoordinate(drawing.time1 as UTCTimestamp)
    const x2 = chart.timeScale().timeToCoordinate(drawing.time2 as UTCTimestamp)
    const y1 = series.priceToCoordinate(drawing.price1)
    const y2 = series.priceToCoordinate(drawing.price2)
    if (x1 === null || x2 === null || y1 === null || y2 === null) {
      continue
    }

    if (drawing.type === 'line' || drawing.type === 'measure' || drawing.type === 'text') {
      lines.push({
        id: drawing.id,
        kind: drawing.type,
        label: drawing.label,
        x1,
        x2,
        y1,
        y2,
        color: drawing.color,
        opacity: drawing.opacity,
        dashed: drawing.dashed,
        lineWidth: drawing.lineWidth,
        selected: selectedId === drawing.id,
      })
      continue
    }

    if (drawing.type === 'halfbox') {
      const midPrice = (drawing.price1 + drawing.price2) / 2
      const midY = series.priceToCoordinate(midPrice)
      boxes.push(toBox(drawing, x1, x2, y1, y2, 'halfbox', '', selectedId, {
        level0Y: y1,
        midY: midY ?? undefined,
        level1Y: y2,
      }))
      continue
    }

    if (drawing.type === 'sltp') {
      const entryY = series.priceToCoordinate((drawing.price1 + drawing.price2) / 2)
      boxes.push(toBox(drawing, x1, x2, y1, entryY ?? y2, 'trade-risk', 'Stop Loss', selectedId))
      boxes.push(toBox(drawing, x1, x2, entryY ?? y1, y2, 'trade-reward', 'Take Profit', selectedId))
      if (entryY !== null) {
        lines.push({ id: `${drawing.id}-entry`, kind: 'entry', label: 'Entry', x1, x2, y1: entryY, y2: entryY, color: '#111827', selected: false })
      }
      continue
    }

    boxes.push(toBox(drawing, x1, x2, y1, y2, 'fvg', drawing.label, selectedId))
  }

  return { boxes, lines }
}

function addHalfBox(
  boxes: OverlayBox[],
  annotation: ChartAnnotationDto,
  chart: IChartApi,
  series: ISeriesApi<'Candlestick'>,
  id: string,
  selectedId: string | null,
) {
  if (annotation.secondaryPrice === null || annotation.secondaryPrice === undefined || annotation.tertiaryPrice === null || annotation.tertiaryPrice === undefined) {
    return
  }

  const x1 = chart.timeScale().timeToCoordinate(toTimestamp(annotation.startTimestamp))
  const x2 = chart.timeScale().timeToCoordinate(toTimestamp(annotation.endTimestamp))
  const level0Y = series.priceToCoordinate(annotation.price)
  const midY = series.priceToCoordinate(annotation.secondaryPrice)
  const level1Y = series.priceToCoordinate(annotation.tertiaryPrice)
  if (x1 === null || x2 === null || level0Y === null || midY === null || level1Y === null) {
    return
  }

  boxes.push({
    id,
    kind: 'halfbox',
    label: '',
    direction: annotation.direction,
    left: Math.min(x1, x2),
    top: Math.min(level0Y, level1Y),
    width: Math.max(12, Math.abs(x2 - x1)),
    height: Math.max(4, Math.abs(level1Y - level0Y)),
    level0Y,
    midY,
    level1Y,
    selected: selectedId === id,
  })
}

function addPriceBox(
  boxes: OverlayBox[],
  series: ISeriesApi<'Candlestick'>,
  x1: number,
  x2: number,
  priceA: number,
  priceB: number,
  direction: string,
  kind: string,
  label: string,
  color: string,
) {
  const y1 = series.priceToCoordinate(priceA)
  const y2 = series.priceToCoordinate(priceB)
  if (y1 === null || y2 === null) {
    return
  }

  boxes.push({
    id: `backend-${kind}-${x1}-${y1}`,
    kind,
    label,
    direction,
    left: Math.min(x1, x2),
    top: Math.min(y1, y2),
    width: Math.max(16, Math.abs(x2 - x1)),
    height: Math.max(4, Math.abs(y2 - y1)),
    color,
  })
}

function toBox(
  drawing: Drawing,
  x1: number,
  x2: number,
  y1: number,
  y2: number,
  kind: string,
  label: string,
  selectedId: string | null,
  extras: Partial<OverlayBox> = {},
): OverlayBox {
  return {
    id: drawing.id,
    kind,
    label,
    direction: 'Manual',
    left: Math.min(x1, x2),
    top: Math.min(y1, y2),
    width: Math.max(4, Math.abs(x2 - x1)),
    height: Math.max(4, Math.abs(y2 - y1)),
    color: drawing.color,
    lineWidth: drawing.lineWidth,
    opacity: drawing.opacity,
    dashed: drawing.dashed,
    selected: selectedId === drawing.id,
    hidden: drawing.hidden,
    ...extras,
  }
}

function createDrawing(id: string, tool: Tool, point: DataPoint): Drawing {
  const defaults = {
    id,
    time1: point.time,
    time2: point.time + 60,
    price1: point.price,
    price2: point.price,
    lineWidth: 2,
    opacity: 0.35,
    dashed: false,
    locked: false,
    hidden: false,
  }

  if (tool === 'line' || tool === 'measure') {
    return { ...defaults, type: tool, color: '#2563eb', opacity: 1, label: tool === 'line' ? 'BOS' : 'Measure' }
  }

  if (tool === 'halfbox') {
    return { ...defaults, type: 'halfbox', color: '#111827', price2: point.price - 20, label: '' }
  }

  if (tool === 'sltp') {
    return { ...defaults, type: 'sltp', color: '#111827', price2: point.price - 30, label: '' }
  }

  if (tool === 'text') {
    return { ...defaults, type: 'text', color: '#111827', opacity: 1, label: 'Text' }
  }

  return { ...defaults, type: 'box', color: '#7c3aed', price2: point.price - 15, label: 'FVG' }
}

function resizeDrawing(drawing: Drawing, handle: string, point: DataPoint): Drawing {
  if (handle === 'start' || handle === 'nw' || handle === 'sw') {
    return { ...drawing, time1: point.time, price1: point.price }
  }

  if (handle === 'ne') {
    return { ...drawing, time2: point.time, price1: point.price }
  }

  if (handle === 'se' || handle === 'end') {
    return { ...drawing, time2: point.time, price2: point.price }
  }

  return { ...drawing, time1: point.time, price2: point.price }
}

function normalizeTime(time: Time): number {
  if (typeof time === 'number') {
    return time
  }

  if (typeof time === 'string') {
    return Math.floor(new Date(time).getTime() / 1000)
  }

  return Math.floor(Date.UTC(time.year, time.month - 1, time.day) / 1000)
}

function nearestPriceForTime(candles: CandleDto[], time: Time): number | null {
  const target = normalizeTime(time)
  let best: CandleDto | null = null
  let bestDistance = Number.POSITIVE_INFINITY
  for (const candle of candles) {
    const distance = Math.abs(toTimestamp(candle.timestamp) - target)
    if (distance < bestDistance) {
      best = candle
      bestDistance = distance
    }
  }

  return best?.close ?? null
}

function nearestIndexForTime(candles: CandleDto[], timestamp: number): number {
  if (candles.length === 0 || Number.isNaN(timestamp)) {
    return 0
  }

  const target = Math.floor(timestamp / 1000)
  return candles.reduce((bestIndex, candle, candleIndex) => {
    const currentDistance = Math.abs(toTimestamp(candle.timestamp) - target)
    const bestDistance = Math.abs(toTimestamp(candles[bestIndex].timestamp) - target)
    return currentDistance < bestDistance ? candleIndex : bestIndex
  }, 0)
}

function formatAxisTime(time: Time) {
  const timestamp = normalizeTime(time)
  const date = new Date(timestamp * 1000)
  if (date.getUTCHours() === 0 && date.getUTCMinutes() === 0) {
    return String(date.getUTCDate())
  }

  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })
}

function formatCrosshairTime(time: Time) {
  const timestamp = normalizeTime(time)
  const date = new Date(timestamp * 1000)
  return date.toLocaleString([], {
    weekday: 'short',
    month: 'short',
    day: '2-digit',
    year: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

function formatOhlc(candle: CandleDto) {
  const change = candle.close - candle.open
  const sign = change >= 0 ? '+' : ''
  return `O ${candle.open.toFixed(2)}  H ${candle.high.toFixed(2)}  L ${candle.low.toFixed(2)}  C ${candle.close.toFixed(2)}  ${sign}${change.toFixed(2)}`
}

function compactVolume(volume: number) {
  if (volume >= 1_000_000) {
    return `${(volume / 1_000_000).toFixed(2)}M`
  }

  if (volume >= 1_000) {
    return `${(volume / 1_000).toFixed(2)}K`
  }

  return volume.toLocaleString()
}

function visibleBarsForTimeframe(timeframe: string) {
  if (timeframe === '1m') {
    return 160
  }

  if (timeframe === '5m') {
    return 120
  }

  return 96
}
