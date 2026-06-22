type Props = {
  title: string
  subtitle?: string
  volume?: string
  status?: string
  onReset: () => void
  onFit: () => void
  onLatest: () => void
}

export function ChartToolbar({ title, subtitle, volume, status, onReset, onFit, onLatest }: Props) {
  return (
    <div className="chart-toolbar">
      <div className="chart-identity">
        <div className="symbol-line">
          <span className="symbol-dot">100</span>
          <strong>{title}</strong>
          {subtitle && <span className="ohlc-line">{subtitle}</span>}
          {status && <span className="inline-status">{status}</span>}
        </div>
        <div className="quote-row">
          {volume && <span className="volume">Vol <b>{volume}</b></span>}
        </div>
      </div>
      <div className="chart-actions">
        <button type="button" onClick={onReset}>Reset</button>
        <button type="button" onClick={onFit}>Fit</button>
        <button type="button" onClick={onLatest}>Latest</button>
      </div>
    </div>
  )
}
