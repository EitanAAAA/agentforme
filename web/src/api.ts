import * as signalR from '@microsoft/signalr'
import type {
  AppSettingsDto,
  CandleDto,
  DataStatusDto,
  NqOneMinuteAnalysisDto,
  SmtEventDto,
} from './types'

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000'

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`)
  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText}`)
  }

  return response.json() as Promise<T>
}

export const api = {
  getStatus: () => getJson<DataStatusDto>('/api/status'),
  getSettings: () => getJson<AppSettingsDto>('/api/settings'),
  updateSettings: async (settings: AppSettingsDto) => {
    const response = await fetch(`${API_BASE}/api/settings`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    })
    if (!response.ok) {
      throw new Error(`${response.status} ${response.statusText}`)
    }
    return response.json() as Promise<AppSettingsDto>
  },
  getCandles: (symbol: 'ES' | 'NQ', timeframe = '15m') =>
    getJson<CandleDto[]>(`/api/candles?symbol=${symbol}&timeframe=${timeframe}`),
  getSmtEvents: () => getJson<SmtEventDto[]>('/api/smt-events'),
  getSmtEvent: (id: string) => getJson<SmtEventDto>(`/api/smt-events/${encodeURIComponent(id)}`),
  getFocusedAnalysis: (id: string) =>
    getJson<NqOneMinuteAnalysisDto>(`/api/smt-events/${encodeURIComponent(id)}/nq-1m-analysis`),
}

export function createMarketConnection() {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE}/hubs/market`)
    .withAutomaticReconnect()
    .build()
}
