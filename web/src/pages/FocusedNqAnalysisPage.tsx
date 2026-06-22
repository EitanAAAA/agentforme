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

export function FocusedNqAnalysisPage({ event, analysis, show, onToggle, onBack }: Props) {
  const annotations = (analysis?.annotations ?? []).filter((annotation) => {
    if (annotation.kind === 'Bos') return show.bos
    if (annotation.kind === 'Fvg') return show.fvg
    if (annotation.kind === 'Ifvg') return show.ifvg
    if (annotation.kind === 'HalfBox') return show.halfBox
    if (annotation.kind === 'StopTakeProfit') return show.slTp
    return true
  })

  return (
    <main className="focused-page">
      <div className="focused-header">
        <div>
          <button type="button" className="ghost" onClick={onBack}>Back</button>
          <h1>NQ 1m Focus</h1>
          <p>{event.direction} {event.setupType} | {new Date(event.timestamp).toLocaleString()}</p>
          <span>{analysis?.summary ?? 'Loading focused NQ 1m analysis...'}</span>
        </div>
        <div className="toggle-strip">
          <button className={show.bos ? 'active' : ''} type="button" onClick={() => onToggle('bos')}>Show BOS</button>
          <button className={show.fvg ? 'active' : ''} type="button" onClick={() => onToggle('fvg')}>Show FVG</button>
          <button className={show.ifvg ? 'active' : ''} type="button" onClick={() => onToggle('ifvg')}>Show IFVG</button>
          <button className={show.halfBox ? 'active' : ''} type="button" onClick={() => onToggle('halfBox')}>Show 0.5 Box</button>
          <button className={show.slTp ? 'active' : ''} type="button" onClick={() => onToggle('slTp')}>Show SL/TP</button>
        </div>
      </div>
      <ChartPanel
        symbol="NQ"
        timeframe="1m"
        candles={analysis?.candles ?? []}
        annotations={annotations}
        statusText="DELAYED DATA - focused NQ 1m"
        compact
      />
    </main>
  )
}
