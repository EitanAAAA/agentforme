import type { AppSettingsDto } from '../types'

type Props = {
  open: boolean
  settings?: AppSettingsDto | null
  onClose: () => void
  onChange: (settings: AppSettingsDto) => void
}

export function SettingsDrawer({ open, settings, onClose, onChange }: Props) {
  if (!open || !settings) {
    return null
  }

  return (
    <div className="settings-drawer">
      <div className="drawer-panel">
        <div className="drawer-title">
          <h2>Settings</h2>
          <button type="button" onClick={onClose}>Close</button>
        </div>
        <label>
          Timeframe
          <select value={settings.timeframe} onChange={(event) => onChange({ ...settings, timeframe: event.target.value })}>
            <option value="15m">15m</option>
            <option value="5m">5m</option>
            <option value="1m">1m</option>
          </select>
        </label>
        <label>
          Detection mode
          <select value={settings.detectionMode} onChange={(event) => onChange({ ...settings, detectionMode: event.target.value })}>
            <option value="Both">Both</option>
            <option value="CloseConfirmation">Close confirmation</option>
            <option value="WickBreak">Wick break</option>
          </select>
        </label>
        <label>
          Swing strength
          <input type="number" min={1} max={6} value={settings.swingStrength} onChange={(event) => onChange({ ...settings, swingStrength: Number(event.target.value) })} />
        </label>
        <label>
          Refresh seconds
          <input type="number" min={15} max={300} value={settings.refreshRateSeconds} onChange={(event) => onChange({ ...settings, refreshRateSeconds: Number(event.target.value) })} />
        </label>
        <label>
          Risk buffer points
          <input type="number" min={0.25} max={100} step={0.25} value={settings.riskBufferPoints} onChange={(event) => onChange({ ...settings, riskBufferPoints: Number(event.target.value) })} />
        </label>
        <label className="check-row">
          <input type="checkbox" checked={settings.showBullishSmt} onChange={(event) => onChange({ ...settings, showBullishSmt: event.target.checked })} />
          Show bullish SMT
        </label>
        <label className="check-row">
          <input type="checkbox" checked={settings.showBearishSmt} onChange={(event) => onChange({ ...settings, showBearishSmt: event.target.checked })} />
          Show bearish SMT
        </label>
      </div>
    </div>
  )
}
